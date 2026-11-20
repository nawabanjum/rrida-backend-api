using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <inheritdoc />
    public class SnapshotEmailLogService : ISnapshotEmailLogService
    {
        // App-settings key holding the comma-separated recipient list, e.g.
        // "SnapshotEmailLogging:Recipients": "a@x.com, b@x.com, c@x.com".
        private const string RecipientsConfigKey = "SnapshotEmailLogging:Recipients";

        private static readonly JsonSerializerOptions PrettyJson = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        };

        // Process-wide guard so each snapshot sends at most ONE final-status email
        // even though the Status endpoint is polled repeatedly. Trade-off: an app
        // restart (or a second instance) clears this and could resend once — an
        // acceptable duplicate for a troubleshooting log, never a missed one.
        private static readonly ConcurrentDictionary<int, byte> _finalSent = new();

        private readonly SendGridSettings _settings;
        private readonly IConfiguration _config;
        private readonly ILogger<SnapshotEmailLogService> _logger;

        public SnapshotEmailLogService(
            SendGridSettings settings,
            IConfiguration config,
            ILogger<SnapshotEmailLogService> logger)
        {
            _settings = settings;
            _config = config;
            _logger = logger;
        }

        public Task SendRunLogAsync(object? request, object? response, bool success, string? error)
        {
            var subject = $"[Snapshot Run] {(success ? "SUCCESS" : "FAILED")} – {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
            var html = BuildHtml("RRIDA Personal Snapshot – Run", null, null, success, error, request, response);
            return SendAsync(subject, html, "Run");
        }

        public Task<bool> SendFinalStatusLogOnceAsync(int snapshotId, string status, object? request, object? response, bool success, string? error)
        {
            // Only the terminal states carry the final result worth emailing.
            var isTerminal = string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase);
            if (!isTerminal)
                return Task.FromResult(false);

            // De-dupe: first terminal poll wins, the rest are no-ops.
            if (!_finalSent.TryAdd(snapshotId, 1))
                return Task.FromResult(false);

            return SendFinalStatusInnerAsync(snapshotId, status, request, response, success, error);
        }

        private async Task<bool> SendFinalStatusInnerAsync(int snapshotId, string status, object? request, object? response, bool success, string? error)
        {
            try
            {
                var subject = $"[Snapshot Status] #{snapshotId} {status} – {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
                var html = BuildHtml("RRIDA Personal Snapshot – Final Status", snapshotId, status, success, error, request, response);
                await SendAsync(subject, html, $"Status #{snapshotId}");
                return true;
            }
            catch
            {
                // SendAsync already swallows + logs; this guards the build step too.
                // Allow a later poll to retry by releasing the de-dupe slot.
                _finalSent.TryRemove(snapshotId, out _);
                return false;
            }
        }

        private async Task SendAsync(string subject, string html, string label)
        {
            var recipients = GetRecipients();
            if (recipients.Count == 0)
            {
                _logger.LogWarning(
                    "[SnapshotEmailLog] No recipients configured ({Key}); skipping {Label} email.",
                    RecipientsConfigKey, label);
                return;
            }

            try
            {
                var ok = await EmailHelper.SendRawHtmlEmailAsync(_settings, recipients, subject, html);
                if (ok)
                    _logger.LogInformation("[SnapshotEmailLog] Sent {Label} email to {Count} recipient(s).", label, recipients.Count);
                else
                    _logger.LogError(
                        "[SnapshotEmailLog] {Label} email was NOT sent. UseSendGrid={UseSendGrid}, " +
                        "SendGridKeySet={SgKey}, ResendKeySet={ResendKey}, From={From}, Recipients={Recipients}.",
                        label, _settings.UseSendGrid,
                        !string.IsNullOrWhiteSpace(_settings.ApiKey),
                        !string.IsNullOrWhiteSpace(_settings.ResendApiKey),
                        _settings.FromEmail, string.Join(", ", recipients));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SnapshotEmailLog] {Label} email threw and was suppressed.", label);
            }
        }

        private List<string> GetRecipients()
        {
            var raw = _config[RecipientsConfigKey];
            if (string.IsNullOrWhiteSpace(raw))
                return new List<string>();

            return raw
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => e.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildHtml(
            string title, int? snapshotId, string? status,
            bool success, string? error, object? request, object? response)
        {
            string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
            string Json(object? o)
            {
                if (o == null) return "null";
                try { return JsonSerializer.Serialize(o, PrettyJson); }
                catch (Exception ex) { return $"<could not serialize: {ex.Message}>"; }
            }

            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:13px;color:#222;\">");
            sb.Append($"<h2 style=\"margin:0 0 12px;\">{Enc(title)}</h2>");

            sb.Append("<table style=\"border-collapse:collapse;margin-bottom:16px;\">");
            void Row(string k, string v) => sb.Append(
                $"<tr><td style=\"padding:3px 12px 3px 0;color:#666;\">{Enc(k)}</td>" +
                $"<td style=\"padding:3px 0;font-weight:600;\">{v}</td></tr>");

            Row("Outcome", success
                ? "<span style=\"color:#1a7f37;\">SUCCESS</span>"
                : "<span style=\"color:#b00020;\">FAILED</span>");
            if (snapshotId.HasValue) Row("Snapshot Id", Enc(snapshotId.Value.ToString()));
            if (!string.IsNullOrWhiteSpace(status)) Row("Status", Enc(status));
            Row("Timestamp (UTC)", Enc(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")));
            Row("Host", Enc(Environment.MachineName));
            if (!string.IsNullOrWhiteSpace(error)) Row("Error", $"<span style=\"color:#b00020;\">{Enc(error)}</span>");
            sb.Append("</table>");

            void Section(string heading, object? body)
            {
                sb.Append($"<h3 style=\"margin:16px 0 6px;\">{Enc(heading)}</h3>");
                sb.Append("<pre style=\"background:#f6f8fa;border:1px solid #e1e4e8;border-radius:6px;" +
                          "padding:12px;white-space:pre-wrap;word-break:break-word;font-size:12px;\">");
                sb.Append(Enc(Json(body)));
                sb.Append("</pre>");
            }

            Section("Request", request);
            Section("Response", response);

            sb.Append("<p style=\"color:#999;margin-top:18px;font-size:11px;\">" +
                      "Automated RRIDA Personal Snapshot diagnostic log.</p>");
            sb.Append("</div>");
            return sb.ToString();
        }
    }
}
