using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using SharpToken;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace CloverBackoffice.API.Middlewares
{
    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuditMiddleware> _logger;
        private readonly IServiceProvider _serviceProvider;


        public AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger, IServiceProvider serviceProvider)
        {
            _next = next;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task Invoke(HttpContext context)
        {
            var skipPaths = new[]
            {
                "/api/profile/list",
                "/api/User/GetUserList",
                "/api/SystemSetting/GetPromptTypes",
                "/api/SystemSetting/GetSystemModule",
                "/api/SystemSetting/GetUserGroups",
                "/api/permissions/matrix",
                "/api/FeedbackAnalysis/upload-file",
                "/api/systemnotification/unread-count"
            };

            if (skipPaths.Any(path => context.Request.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }

            var request = context.Request;
            var requestTime = DateTime.UtcNow;

            // Read request body
            string requestBody = "";
            if (request.ContentLength > 0)
            {
                request.EnableBuffering();
                using var reader = new StreamReader(request.Body, leaveOpen: true);
                requestBody = await reader.ReadToEndAsync();
                request.Body.Position = 0;
            }

            // Only the stored copy is masked; the action still reads the real body.
            requestBody = MaskSecrets(request.Path, requestBody);

            // Capture response body
            var originalBodyStream = context.Response.Body;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await _next(context);

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var responseText = await new StreamReader(context.Response.Body).ReadToEndAsync();
            context.Response.Body.Seek(0, SeekOrigin.Begin);

            using (var scope = _serviceProvider.CreateScope())
            {
                var auditLogsService = scope.ServiceProvider.GetRequiredService<IAuditLogsService>();
                var tokenReader = scope.ServiceProvider.GetRequiredService<ITokenReaderService>();
                int? userId = tokenReader.GetUserIdFromToken();

                // BASE LOG OBJECT
                var log = new AuditLogs
                {
                    Timestamp = requestTime,
                    UserId = userId,
                    Endpoint = request.Path,
                    HttpMethod = request.Method,
                    RequestData = requestBody,
                    ResponseData = responseText,
                    ActionType = GetActionType(request.Method)
                };

                //  APPLY ALL ENRICHMENT (CompanyName, TeamName, UnitName, message rewrite)
                await EnrichAuditLogDataAsync(log, request.Path, requestBody, responseText, scope.ServiceProvider);

                // ENRICH WITH DETAILED LOGS FOR WORKBENCH-RUN
                if (request.Path.Value != null &&
                    request.Path.Value.Contains("workbench-run", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var logCollector = context.RequestServices.GetService<ILogCollector>();
                        if (logCollector != null && logCollector.HasEntries)
                        {
                            var detailedLogs = logCollector.GetAll();
                            var enriched = new StringBuilder();
                            enriched.AppendLine(log.ResponseData);
                            enriched.AppendLine();
                            enriched.AppendLine("===== DETAILED EXECUTION LOGS =====");
                            enriched.Append(detailedLogs);
                            log.ResponseData = enriched.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to enrich audit log with detailed workbench logs");
                    }
                }

                // SAVE FINAL LOG
                try { await auditLogsService.AddAuditLogsAsync(log); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to save audit log"); }


                // EXTRA LOGGING FOR LLM
                if (request.Path.Equals("/api/rag/openai-query", StringComparison.OrdinalIgnoreCase) ||
                    request.Path.Equals("/api/FeedbackAnalysis/upload-prompt-data-file", StringComparison.OrdinalIgnoreCase))
                {
                    var llmLogsService = scope.ServiceProvider.GetRequiredService<IAuditLogsService>();

                    var (fromDate, toDate) = ExtractDateRange(context, requestBody);
                    var finalPrompt = context.Items.ContainsKey("FinalPrompt")
                        ? context.Items["FinalPrompt"]?.ToString()
                        : null;

                    var llmLog = new LLMLog
                    {
                        Timestamp = requestTime,
                        UserId = GetUserIdFromClaims(context.User) ?? 0,
                        Endpoint = request.Path,
                        HttpMethod = request.Method,
                        RequestData = requestBody,
                        ResponseData = responseText,
                        UnitId = ExtractUnitId(context, requestBody),
                        TokenCount = ExtractTokenCount(requestBody, responseText),
                        PromptTokens = finalPrompt,
                        FromDate = fromDate,
                        ToDate = toDate,
                        Status = context.Response.StatusCode.ToString()
                    };

                    try { await llmLogsService.AddLLMLogAsync(llmLog); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to save LLM log"); }
                }
            }

            await responseBody.CopyToAsync(originalBodyStream);
        }

        // Request fields that must never reach AuditLogs.RequestData, per endpoint.
        private static readonly Dictionary<string, string[]> SecretFieldsByPath = new(StringComparer.OrdinalIgnoreCase)
        {
            ["/api/profile/activate-user"] = new[] { "temporaryPassword" }
        };

        private const string MaskedValue = "***";

        /// <summary>
        /// Replaces secret JSON fields of the listed endpoints with "***" (any
        /// casing of the field name). A body that is not a JSON object cannot be
        /// inspected, so none of it is kept.
        /// </summary>
        public static string MaskSecrets(PathString path, string requestBody)
        {
            if (string.IsNullOrEmpty(requestBody)
                || !path.HasValue
                || !SecretFieldsByPath.TryGetValue(path.Value!.TrimEnd('/'), out var fields))
            {
                return requestBody;
            }

            try
            {
                var json = JObject.Parse(requestBody);
                foreach (var property in json.Properties())
                {
                    if (fields.Any(f => string.Equals(f, property.Name, StringComparison.OrdinalIgnoreCase)))
                        property.Value = MaskedValue;
                }
                return json.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                return MaskedValue;
            }
        }

        // ------------------------------------------------------------
        //  ENRICH RESPONSE DATA (TEAM/COMPANY/UNIT NAMES + MESSAGES)
        // ------------------------------------------------------------
        private async Task EnrichAuditLogDataAsync(AuditLogs log, string endpoint, string requestBody, string responseText, IServiceProvider provider)
        {
            try
            {
                // Only enrich for specific endpoints
                var enrichEndpoints = new[]
                {
                    "/api/actionhub/upsert-action-event",
                     "/api/actionhub/upsert-action-hub",
                    "/api/teamhierarchy/upsert-team",
                    "/api/teamhierarchy/assign-user-to-team"
                };

                bool shouldEnrich = enrichEndpoints.Any(e => endpoint.Equals(e, StringComparison.OrdinalIgnoreCase)) ||
                                   endpoint.StartsWith("/api/teamhierarchy/team/", StringComparison.OrdinalIgnoreCase);

                if (!shouldEnrich)
                    return;

                if (string.IsNullOrWhiteSpace(responseText) || !responseText.Trim().StartsWith("{"))
                    return;

                JObject json = JObject.Parse(responseText);

                // Check if response is successful
                bool isSuccess = json["isSuccess"]?.Value<bool>() ?? false;
                if (!isSuccess)
                    return;

                var db = provider.GetRequiredService<ApplicationDbContext>();

                // ============================
                // CASE 1: UPSERT ACTION EVENT
                // ============================
                if (endpoint.Equals("/api/actionhub/upsert-action-event", StringComparison.OrdinalIgnoreCase))
                {
                    // Response structure: { "isSuccess": true, "message": "...", "content": {...} }
                    var content = json["content"] as JObject ?? new JObject();

                    // Extract actionHubId from content
                    if (!int.TryParse(content["actionHubId"]?.ToString(), out int actionHubId) || actionHubId <= 0)
                        return;

                    var hub = await db.ActionHubs.FirstOrDefaultAsync(x => x.Id == actionHubId);
                    if (hub == null)
                        return;

                    var company = await db.Profiles.FirstOrDefaultAsync(x => x.Id == hub.ProfileId);
                    var unit = await db.BusinessUnits.FirstOrDefaultAsync(x => x.Id == hub.UnitId);

                    // Enrich content
                    content["companyId"] = hub.ProfileId;
                    content["companyName"] = company?.ProfileName ?? string.Empty;
                    content["unitId"] = hub.UnitId;
                    content["unitName"] = unit?.Name ?? string.Empty;
                    content["actionHubId"] = actionHubId;

                    json["content"] = content;

                    // Prepare detailed user-friendly message
                    var actionName = content["scheduledEventActivity"]?.ToString() ?? "Unknown activity";
                    var feedbackText = content["feedback"]?.ToString() ?? "No feedback";
                    var actionType = content["type"]?.ToString() ?? "Unknown type";
                    var addedDateStr = content["dateAddedToAction"]?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

                    DateTime.TryParse(addedDateStr, out DateTime addedDate);
                    json["message"] = $"On {addedDate:MMMM dd, yyyy}, the {company?.ProfileName ?? "Unknown"} company {actionType} added a new action {actionName} for the {unit?.Name ?? "Unknown"} unit. Feedback recorded: {feedbackText}.";
                }

                // ============================
                // CASE 5: UPSERT ACTION HUB
                // ============================
                else if (endpoint.Equals("/api/actionhub/upsert-action-hub", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(requestBody))
                        return;

                    JObject requestJson;
                    try
                    {
                        requestJson = JObject.Parse(requestBody);
                    }
                    catch
                    {
                        return;
                    }

                    if (!int.TryParse(requestJson["companyId"]?.ToString(), out int profileId) || profileId <= 0)
                        return;

                    if (!int.TryParse(requestJson["unitId"]?.ToString(), out int unitId) || unitId <= 0)
                        return;

                    string actionTitle = requestJson["actionTitle"]?.ToString() ?? "Unknown Action";
                    string description = requestJson["description"]?.ToString() ?? string.Empty;
                    string actionType = requestJson["type"]?.ToString() ?? "Unknown type";

                    var company = await db.Profiles.FirstOrDefaultAsync(x => x.Id == profileId);
                    var unit = await db.BusinessUnits.FirstOrDefaultAsync(x => x.Id == unitId);

                    // Enrich content (NO user info)
                    var content = json["content"] as JObject ?? new JObject();

                    content["companyId"] = profileId;
                    content["companyName"] = company?.ProfileName ?? string.Empty;
                    content["unitId"] = unitId;
                    content["unitName"] = unit?.Name ?? string.Empty;
                    content["actionTitle"] = actionTitle;
                    content["description"] = description;
                    content["type"] = actionType;

                    json["content"] = content;

                    // User-friendly audit message (NO user reference)
                    json["message"] =
                        $"The {actionTitle} action hub was created under the {unit?.Name ?? "Unknown"} unit of the {company?.ProfileName ?? "Unknown"} company {actionType}";
                }


                // ============================
                // CASE 2: UPSERT TEAM
                // ============================
                else if (endpoint.Equals("/api/teamhierarchy/upsert-team", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(requestBody))
                        return;

                    JObject requestJson;
                    try
                    {
                        requestJson = JObject.Parse(requestBody);
                    }
                    catch
                    {
                        return;
                    }

                    var teamNameToken = requestJson["teamName"];
                    var profileIdToken = requestJson["companyId"];

                    if (teamNameToken == null || profileIdToken == null)
                        return;

                    if (!int.TryParse(profileIdToken.ToString(), out int profileIdInt) || profileIdInt <= 0)
                        return;

                    uint profileId = (uint)profileIdInt;
                    string teamName = CleanTeamName(teamNameToken.ToString());

                    var company = await db.Profiles.FirstOrDefaultAsync(x => x.Id == profileId);
                    if (company == null)
                        return;

                    // Resolve username ONLY if UserId exists
                    string userName = null;
                    if (log.UserId.HasValue)
                    {
                        var user = await db.Users.FirstOrDefaultAsync(x => x.UserId == log.UserId.Value);
                        userName = user?.Name;
                    }

                    // Enrich content
                    var content = json["content"] as JObject ?? new JObject();
                    content["teamName"] = teamName;
                    content["companyId"] = profileId;
                    content["companyName"] = company.ProfileName ?? string.Empty;

                    if (log.UserId.HasValue && userName != null)
                    {
                        content["userId"] = log.UserId.Value;
                        content["userName"] = userName;
                    }

                    json["content"] = content;

                    // Build message safely
                    json["message"] = userName != null
                        ? $"A new team {teamName} was successfully created under the {company.ProfileName} company by {userName}."
                        : $"A new team {teamName} was successfully created under the {company.ProfileName} company.";
                }


                // ============================
                // CASE 3: DELETE TEAM
                // ============================
                else if (endpoint.StartsWith("/api/teamhierarchy/team/", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = endpoint.Split('/');
                    if (parts.Length < 5)
                        return;

                    if (!uint.TryParse(parts[4], out uint teamId) || teamId == 0)
                        return;

                    var team = await db.BusinessUnits
                        .Include(b => b.Profile)
                        .FirstOrDefaultAsync(x => x.Id == teamId);

                    if (team == null)
                        return;

                    // Get user name from log.UserId
                    string userName = "Unknown";
                    if (log.UserId.HasValue)
                    {
                        var user = await db.Users.FirstOrDefaultAsync(x => x.UserId == log.UserId.Value);
                        userName = user?.Name ?? "Unknown";
                    }

                    // Enrich content
                    var content = json["content"] as JObject ?? new JObject();
                    content["teamId"] = teamId;
                    content["teamName"] = CleanTeamName(team.Name ?? string.Empty);
                    content["companyId"] = team.Profile.Id;
                    content["companyName"] = team.Profile.ProfileName ?? string.Empty;
                    if (log.UserId.HasValue)
                    {
                        content["userId"] = log.UserId.Value;
                        content["userName"] = userName;
                    }

                    json["content"] = content;
                    json["message"] = $"The {CleanTeamName(team?.Name) ?? "Unknown"} team of the {team?.Profile?.ProfileName ?? "Unknown"} company was deleted by {userName}.";
                }

                // ============================
                // CASE 4: ASSIGN USER TO TEAM
                // ============================
                else if (endpoint.Equals("/api/teamhierarchy/assign-user-to-team", StringComparison.OrdinalIgnoreCase))
                {
                    var content = json["content"] as JObject ?? new JObject();

                    if (!int.TryParse(content["userId"]?.ToString(), out int userId) || userId <= 0)
                        return;

                    if (!uint.TryParse(content["teamId"]?.ToString(), out uint teamId) || teamId == 0)
                        return;

                    var user = await db.Users.FirstOrDefaultAsync(x => x.UserId == userId);
                    var team = await db.BusinessUnits
                        .Include(b => b.Profile)
                        .FirstOrDefaultAsync(x => x.Id == teamId);

                    if (team == null)
                        return;

                    // Enrich content
                    content["userId"] = userId;
                    content["userName"] = user?.Name ?? string.Empty;
                    content["teamId"] = teamId;
                    content["teamName"] = CleanTeamName(team.Name ?? string.Empty);
                    content["companyId"] = team.Profile.Id;
                    content["companyName"] = team.Profile.ProfileName ?? string.Empty;

                    json["content"] = content;
                    json["message"] = $"{user?.Name ?? "Unknown"} was assigned to the {CleanTeamName(team?.Name) ?? "Unknown"} team of the {team?.Profile?.ProfileName ?? "Unknown"} company.";
                }

                log.ResponseData = json.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit enrichment failed for endpoint: {Endpoint}", endpoint);
            }
        }

        // Helper to clean team name prefix (same as in AggregateDashboardRepository)
        private string CleanTeamName(string teamName)
        {
            if (string.IsNullOrWhiteSpace(teamName)) return teamName;
            var idx = teamName.IndexOf('_');
            if (idx < 0) return teamName;
            var parts = teamName.Split('_');
            if (parts.Length >= 3)
            {
                return string.Join("_", parts.Skip(2));
            }
            return teamName;
        }


        private string GetActionType(string method) =>
            method switch
            {
                "POST" => "CREATE",
                "PUT" => "UPDATE",
                "PATCH" => "UPDATE",
                "DELETE" => "DELETE",
                "GET" => "READ",
                _ => "UNKNOWN",
            };

        private int CountTokens(string text, string model = "gpt-4o")
        {
            try
            {
                if (model == "gpt-4o" || model.StartsWith("gpt-4o"))
                {
                    var encoding = GptEncoding.GetEncoding("cl100k_base");
                    return encoding.Encode(text ?? "").Count;
                }

                var enc = GptEncoding.GetEncodingForModel(model);
                return enc.Encode(text ?? "").Count;
            }
            catch
            {
                // Absolute fallback
                var encoding = GptEncoding.GetEncoding("cl100k_base");
                return encoding.Encode(text ?? "").Count;
            }
        }
        private int ExtractTokenCount(string requestJson, string responseJson)
        {
            try
            {
                // count both request and response tokens
                var requestTokens = CountTokens(requestJson);
                var responseTokens = CountTokens(responseJson);
                return requestTokens + responseTokens;
            }
            catch
            {
                return 0;
            }
        }
        private int ExtractUnitId(HttpContext context, string requestBody)
        {
            // Try query string first
            if (context.Request.Query.TryGetValue("unitId", out var unitIdStr) && int.TryParse(unitIdStr, out var unitId))
            {
                return unitId;
            }

            // If not in query, try JSON body
            if (!string.IsNullOrWhiteSpace(requestBody) && requestBody.TrimStart().StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(requestBody);
                    if (doc.RootElement.TryGetProperty("unitId", out var jsonUnitId))
                    {
                        return jsonUnitId.GetInt32();
                    }
                }
                catch { }
            }

            return 0; // default
        }
        private (DateTime? FromDate, DateTime? ToDate) ExtractDateRange(HttpContext context, string requestBody)
        {
            int days = 0;

            // 1. Try query string
            if (context.Request.Query.TryGetValue("days", out var daysStr) && int.TryParse(daysStr, out var d1))
            {
                days = d1;
            }
            else if (!string.IsNullOrWhiteSpace(requestBody) && requestBody.TrimStart().StartsWith("{"))
            {
                // 2. Try JSON body
                try
                {
                    using var doc = JsonDocument.Parse(requestBody);
                    if (doc.RootElement.TryGetProperty("days", out var jsonDays))
                    {
                        days = jsonDays.GetInt32();
                    }
                }
                catch { }
            }

            if (days > 0)
            {
                var toDate = DateTime.UtcNow;
                var fromDate = toDate.AddDays(-days);
                return (fromDate, toDate);
            }

            return (null, null);
        }
        private int? GetUserIdFromClaims(ClaimsPrincipal user)
        {
            string[] possibleClaims = { "id", "userId", "sub" };

            foreach (var claimName in possibleClaims)
            {
                var value = user.FindFirst(claimName)?.Value;
                if (!string.IsNullOrEmpty(value) && int.TryParse(value, out var id))
                {
                    return id;
                }
            }

            return null;
        }

    }
}
