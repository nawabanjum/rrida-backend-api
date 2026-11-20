using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Services.Interfaces;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Streaming LLM client for EranCoach. Mirrors the request shape / header choices of
    /// the existing CallLlmAsync dispatcher, but sets stream=true, passes tools + prompt-cache
    /// breakpoints, and parses the SSE response into <see cref="CoachStreamEvent"/>s. Supports
    /// the Anthropic Messages API (endpoint contains "/anthropic/", full tool loop + caching)
    /// and Azure OpenAI-style chat/completions (text only). One turn per call; the service
    /// runs the loop.
    /// </summary>
    public class EranCoachAnthropicClient : IEranCoachAnthropicClient
    {
        // Beta flag that enables the 1-hour cache TTL (matches the reference app).
        private const string ExtendedCacheBeta = "extended-cache-ttl-2025-04-11";

        private readonly IHttpClientFactory _httpFactory;

        public EranCoachAnthropicClient(IHttpClientFactory httpFactory)
        {
            _httpFactory = httpFactory;
        }

        public async IAsyncEnumerable<CoachStreamEvent> StreamTurnAsync(
            IReadOnlyList<CoachSystemBlock> systemBlocks,
            IReadOnlyList<CoachMessage> messages,
            IReadOnlyList<object>? tools,
            ModelConfiguration model,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var endpoint = (model.Endpoint ?? "").Trim();
            var isAnthropic = endpoint.Contains("/anthropic/", StringComparison.OrdinalIgnoreCase);
            var maxTokens = model.MaxTokens > 0 ? model.MaxTokens : 4096;

            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            string url;
            string body;

            if (isAnthropic)
            {
                client.DefaultRequestHeaders.Add("x-api-key", model.ApiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                client.DefaultRequestHeaders.Add("anthropic-beta", ExtendedCacheBeta);

                var bodyDict = new Dictionary<string, object?>
                {
                    ["model"] = model.ModelName?.Trim(),
                    ["system"] = BuildAnthropicSystem(systemBlocks),
                    ["messages"] = ToAnthropicMessages(messages),
                    ["max_tokens"] = maxTokens,
                    ["stream"] = true,
                };
                // Newer Claude models (Opus 4.7+, Sonnet 5+, Opus 5.5) reject temperature/top_p with a 400.
                AnthropicModelRules.AddSampling(bodyDict, model.ModelName, model.Temperature, model.TopP);
                if (tools is { Count: > 0 }) bodyDict["tools"] = tools;

                url = endpoint;
                body = JsonSerializer.Serialize(bodyDict);
            }
            else
            {
                client.DefaultRequestHeaders.Add("api-key", model.ApiKey);
                var oaMessages = ToOpenAiMessages(systemBlocks, messages);

                bool isServerless = endpoint.Contains("/models/chat/completions", StringComparison.OrdinalIgnoreCase)
                    || (endpoint.Contains(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
                        && endpoint.Contains("/models/", StringComparison.OrdinalIgnoreCase));

                if (isServerless)
                {
                    url = endpoint;
                    body = JsonSerializer.Serialize(new
                    {
                        model = model.ModelName?.Trim(),
                        messages = oaMessages,
                        max_tokens = maxTokens,
                        temperature = model.Temperature,
                        top_p = model.TopP,
                        stream = true,
                    });
                }
                else
                {
                    client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
                    url = $"/openai/deployments/{model.ModelName?.Trim()}/chat/completions?api-version={model.ApiVersion}";
                    body = JsonSerializer.Serialize(new
                    {
                        messages = oaMessages,
                        max_tokens = maxTokens,
                        temperature = model.Temperature,
                        top_p = model.TopP,
                        stream = true,
                    });
                }
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Accept.ParseAdd("text/event-stream");

            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"EranCoach LLM stream failed ({(int)res.StatusCode}): {Truncate(err, 600)}");
            }

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            // Per-turn SSE state (Anthropic blocks stream across several events).
            var turn = new AnthropicTurn();
            string? stopReason = null;

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line.Substring(5).Trim();
                if (data.Length == 0) continue;
                if (data == "[DONE]") break; // OpenAI terminator

                var emit = new List<CoachStreamEvent>(1);
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    if (isAnthropic)
                        ParseAnthropicEvent(root, turn, emit, ref stopReason);
                    else
                        ParseOpenAiEvent(root, emit, ref stopReason);
                }
                catch (JsonException)
                {
                    // keep-alive / partial line — ignore
                }

                foreach (var e in emit)
                    yield return e;
            }

            // The whole turn in the model's own order, thinking blocks included, so a tool loop can
            // send it back unchanged (Claude Opus 5.5 always thinks, and its blocks must be replayed).
            if (isAnthropic && turn.Done.Count > 0)
                yield return new CoachAssistantContent(new List<CoachBlock>(turn.Done.Values));

            yield return new CoachTurnEnded(stopReason);
        }

        /// <summary>One Anthropic turn's blocks as they stream in, keyed by their index.</summary>
        private sealed class AnthropicTurn
        {
            public readonly Dictionary<int, (string Id, string Name)> ToolInfo = new();
            public readonly Dictionary<int, StringBuilder> ToolJson = new();
            public readonly Dictionary<int, StringBuilder> Text = new();
            public readonly Dictionary<int, (StringBuilder Thinking, StringBuilder Signature)> Thinking = new();
            public readonly SortedDictionary<int, CoachBlock> Done = new();
        }

        public async Task<string> CompleteAsync(
            string systemPrompt, string userContent, ModelConfiguration model, int maxTokens, CancellationToken ct = default,
            bool failWhenCutOff = false)
        {
            var endpoint = (model.Endpoint ?? "").Trim();
            var isAnthropic = endpoint.Contains("/anthropic/", StringComparison.OrdinalIgnoreCase);
            var max = maxTokens > 0 ? maxTokens : 1024;

            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(2);

            string url, body;
            if (isAnthropic)
            {
                client.DefaultRequestHeaders.Add("x-api-key", model.ApiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                var bodyDict = new Dictionary<string, object?>
                {
                    ["model"] = model.ModelName?.Trim(),
                    ["system"] = systemPrompt,
                    ["messages"] = new[] { new { role = "user", content = userContent } },
                    ["max_tokens"] = max,
                };
                if (AnthropicModelRules.AcceptsSampling(model.ModelName)) bodyDict["temperature"] = model.Temperature;
                url = endpoint;
                body = JsonSerializer.Serialize(bodyDict);
            }
            else
            {
                client.DefaultRequestHeaders.Add("api-key", model.ApiKey);
                var messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                };
                bool isServerless = endpoint.Contains("/models/chat/completions", StringComparison.OrdinalIgnoreCase)
                    || (endpoint.Contains(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
                        && endpoint.Contains("/models/", StringComparison.OrdinalIgnoreCase));
                if (isServerless)
                {
                    url = endpoint;
                    body = JsonSerializer.Serialize(new { model = model.ModelName?.Trim(), messages, max_tokens = max, temperature = model.Temperature });
                }
                else
                {
                    client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
                    url = $"/openai/deployments/{model.ModelName?.Trim()}/chat/completions?api-version={model.ApiVersion}";
                    body = JsonSerializer.Serialize(new { messages, max_tokens = max, temperature = model.Temperature });
                }
            }

            using var res = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"EranCoach completion failed ({(int)res.StatusCode}): {Truncate(text, 400)}");

            using var d = JsonDocument.Parse(text);
            var root = d.RootElement;
            if (isAnthropic)
            {
                var stop = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;
                if (failWhenCutOff && stop == "refusal")
                    throw new InvalidOperationException("the model declined this request");
                if (failWhenCutOff && stop == "max_tokens")
                    throw new InvalidOperationException("the response hit max_tokens and was truncated");

                // Every text block, joined, as the MVP reads a response.
                var answer = new StringBuilder();
                if (root.TryGetProperty("content", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var blk in arr.EnumerateArray())
                        if (blk.TryGetProperty("type", out var ty) && ty.GetString() == "text"
                            && blk.TryGetProperty("text", out var tx))
                            answer.Append(tx.GetString());
                return answer.ToString().Trim();
            }
            var choice = root.GetProperty("choices")[0];
            var finish = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
            if (failWhenCutOff && finish == "content_filter")
                throw new InvalidOperationException("the model declined this request");
            if (failWhenCutOff && finish == "length")
                throw new InvalidOperationException("the response hit max_tokens and was truncated");
            return choice.GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        private static string Str(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static void ParseAnthropicEvent(
            JsonElement root,
            AnthropicTurn turn,
            List<CoachStreamEvent> emit,
            ref string? stopReason)
        {
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "content_block_start":
                {
                    var index = root.GetProperty("index").GetInt32();
                    var cb = root.GetProperty("content_block");
                    switch (Str(cb, "type"))
                    {
                        case "tool_use":
                            turn.ToolInfo[index] = (Str(cb, "id"), Str(cb, "name"));
                            turn.ToolJson[index] = new StringBuilder();
                            break;
                        case "text":
                            turn.Text[index] = new StringBuilder(Str(cb, "text"));
                            break;
                        case "thinking":
                            turn.Thinking[index] = (new StringBuilder(Str(cb, "thinking")), new StringBuilder(Str(cb, "signature")));
                            break;
                        case "redacted_thinking":
                            turn.Done[index] = new CoachRedactedThinkingBlock(Str(cb, "data"));
                            break;
                    }
                    break;
                }
                case "content_block_delta":
                {
                    var index = root.GetProperty("index").GetInt32();
                    var delta = root.GetProperty("delta");
                    switch (Str(delta, "type"))
                    {
                        case "text_delta":
                            var text = Str(delta, "text");
                            emit.Add(new CoachTextDelta(text));
                            if (turn.Text.TryGetValue(index, out var tb)) tb.Append(text);
                            break;
                        case "input_json_delta":
                            if (turn.ToolJson.TryGetValue(index, out var sb)) sb.Append(Str(delta, "partial_json"));
                            break;
                        case "thinking_delta":
                            if (turn.Thinking.TryGetValue(index, out var th)) th.Thinking.Append(Str(delta, "thinking"));
                            break;
                        case "signature_delta":
                            if (turn.Thinking.TryGetValue(index, out var sg)) sg.Signature.Append(Str(delta, "signature"));
                            break;
                    }
                    break;
                }
                case "content_block_stop":
                {
                    var index = root.GetProperty("index").GetInt32();
                    if (turn.ToolInfo.TryGetValue(index, out var info))
                    {
                        var json = turn.ToolJson.TryGetValue(index, out var sb) ? sb.ToString() : "{}";
                        if (string.IsNullOrWhiteSpace(json)) json = "{}";
                        emit.Add(new CoachToolUseReady(info.Id, info.Name, json));
                        turn.Done[index] = new CoachToolUseBlock(info.Id, info.Name, json);
                    }
                    else if (turn.Text.TryGetValue(index, out var tb) && tb.Length > 0)
                    {
                        turn.Done[index] = new CoachTextBlock(tb.ToString());
                    }
                    else if (turn.Thinking.TryGetValue(index, out var th))
                    {
                        turn.Done[index] = new CoachThinkingBlock(th.Thinking.ToString(), th.Signature.ToString());
                    }
                    break;
                }
                case "message_delta":
                {
                    if (root.TryGetProperty("delta", out var d) && d.TryGetProperty("stop_reason", out var sr)
                        && sr.ValueKind == JsonValueKind.String)
                        stopReason = sr.GetString();
                    break;
                }
            }
        }

        private static void ParseOpenAiEvent(JsonElement root, List<CoachStreamEvent> emit, ref string? stopReason)
        {
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return;

            var first = choices[0];
            if (first.TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
                emit.Add(new CoachTextDelta(content.GetString() ?? ""));

            if (first.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                stopReason = fr.GetString();
        }

        // ── Wire serialization ──

        // System as an array of cache-aware text blocks (Anthropic). Blocks with a TTL get a
        // cache_control breakpoint so the persona/catalog/team-context aren't re-billed each turn.
        // Anthropic allows at most 4 breakpoints per request (the message history uses one), so only
        // the first and last cacheable blocks get one: the prefix cache covers everything between,
        // and the first keeps the persona cached when a later block (e.g. memory) changes.
        private static List<object> BuildAnthropicSystem(IReadOnlyList<CoachSystemBlock> blocks)
        {
            var firstCached = -1;
            var lastCached = -1;
            for (var i = 0; i < blocks.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(blocks[i].CacheTtl)) continue;
                if (firstCached < 0) firstCached = i;
                lastCached = i;
            }

            var arr = new List<object>(blocks.Count);
            for (var i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (i == firstCached || i == lastCached)
                    arr.Add(new { type = "text", text = b.Text, cache_control = new { type = "ephemeral", ttl = b.CacheTtl } });
                else
                    arr.Add(new { type = "text", text = b.Text });
            }
            return arr;
        }

        private static List<object> ToAnthropicMessages(IReadOnlyList<CoachMessage> messages)
        {
            // Cache the conversation prefix up to the last prior message (5-minute breakpoint),
            // so re-sending the history across turns / tool-loop hops isn't re-billed.
            var cacheIndex = messages.Count - 2;

            var list = new List<object>(messages.Count);
            for (var mi = 0; mi < messages.Count; mi++)
            {
                var m = messages[mi];
                var blocks = new List<Dictionary<string, object?>>(m.Content.Count);
                foreach (var b in m.Content)
                {
                    switch (b)
                    {
                        case CoachTextBlock tb:
                            blocks.Add(new Dictionary<string, object?> { ["type"] = "text", ["text"] = tb.Text });
                            break;
                        case CoachToolUseBlock tu:
                            blocks.Add(new Dictionary<string, object?> { ["type"] = "tool_use", ["id"] = tu.Id, ["name"] = tu.Name, ["input"] = ParseInput(tu.InputJson) });
                            break;
                        case CoachToolResultBlock tr:
                            blocks.Add(new Dictionary<string, object?> { ["type"] = "tool_result", ["tool_use_id"] = tr.ToolUseId, ["content"] = tr.Content });
                            break;
                        // Sent back exactly as the model wrote them (Claude Opus 5.5 and later require it).
                        case CoachThinkingBlock th:
                            blocks.Add(new Dictionary<string, object?> { ["type"] = "thinking", ["thinking"] = th.Thinking, ["signature"] = th.Signature });
                            break;
                        case CoachRedactedThinkingBlock rt:
                            blocks.Add(new Dictionary<string, object?> { ["type"] = "redacted_thinking", ["data"] = rt.Data });
                            break;
                    }
                }
                // A cache breakpoint cannot sit on a thinking block.
                if (mi == cacheIndex && blocks.Count > 0
                    && blocks[^1]["type"] is not ("thinking" or "redacted_thinking"))
                    blocks[^1]["cache_control"] = new { type = "ephemeral", ttl = "5m" };

                list.Add(new Dictionary<string, object?> { ["role"] = m.Role, ["content"] = blocks });
            }
            return list;
        }

        private static List<object> ToOpenAiMessages(IReadOnlyList<CoachSystemBlock> systemBlocks, IReadOnlyList<CoachMessage> messages)
        {
            var system = string.Join("\n\n", Texts(systemBlocks));
            var list = new List<object> { new { role = "system", content = system } };
            foreach (var m in messages)
            {
                var sb = new StringBuilder();
                foreach (var b in m.Content)
                    if (b is CoachTextBlock tb) sb.Append(tb.Text);
                list.Add(new { role = m.Role, content = sb.ToString() });
            }
            return list;
        }

        private static IEnumerable<string> Texts(IReadOnlyList<CoachSystemBlock> blocks)
        {
            foreach (var b in blocks) yield return b.Text;
        }

        private static object ParseInput(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new { };
            try { return JsonSerializer.Deserialize<JsonElement>(json); }
            catch { return new { }; }
        }

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s!.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
