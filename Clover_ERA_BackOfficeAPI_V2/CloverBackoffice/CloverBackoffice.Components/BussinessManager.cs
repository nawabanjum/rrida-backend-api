using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CloverBackoffice.Components
{
    public static class BussinessManager
    {
        public static byte[] Key { get; private set; }
        public static byte[] IV { get; private set; }
        private static SendGridSettings _sendGridSettings;

        //  Call this in Program.cs to initialize Key/IV from config
        public static void Initialize(IConfiguration configuration, SendGridSettings sendGridSettings)
        {
            var keyString = configuration[configuration["AzureKeyVault:SecretNames:EncryptionKey"]];
            var ivString = configuration[configuration["AzureKeyVault:SecretNames:EncryptionIV"]];

            if (string.IsNullOrWhiteSpace(keyString) || string.IsNullOrWhiteSpace(ivString))
                throw new Exception("Encryption Key or IV is missing or empty.");

            Key = Convert.FromBase64String(keyString);
            IV = Convert.FromBase64String(ivString);

            // Store SendGrid settings
            _sendGridSettings = sendGridSettings;
        }

        public static string Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = Key;
            aes.IV = IV;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
            using var sw = new StreamWriter(cs);
            sw.Write(plainText);
            sw.Close();

            return WebEncoders.Base64UrlEncode(ms.ToArray());
        }

        public static string Decrypt(string cipherText)
        {
            var buffer = WebEncoders.Base64UrlDecode(cipherText);

            using var aes = Aes.Create();
            aes.Key = Key;
            aes.IV = IV;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(buffer);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }

        public static string EncryptObject(object obj)
        {
            var json = JsonSerializer.Serialize(obj);
            return Encrypt(json);
        }

        public static T DecryptObject<T>(string encrypted)
        {
            var json = Decrypt(encrypted);
            return JsonSerializer.Deserialize<T>(json);
        }

        //SendEmail method centralized here
        public static async Task<bool> SendEmailAsync(SendGridSettings sendGridSettings, string email, int userId, int profileId)
        {
            var toEmail = email;
            var baseUrl = sendGridSettings.FrontendBaseUrl;

            var payload = new
            {
                Email = email,
                UserId = userId,
                RequestTime = DateTime.UtcNow,
            };

            var encrypted = EncryptObject(payload);
            var verifyLink = $"{baseUrl}/verifyCompany?data={encrypted}";

            var dynamicData = new Dictionary<string, object>
             {
                 { "verify_link", verifyLink }
             };

            return await EmailHelper.SendEmailAsync(
                sendGridSettings,
                toEmail,
                "Verify Your Email \u2013 RRIDA",
                "CompanyVerification",
                sendGridSettings.TemplateId,
                dynamicData
            );
        }

        //contact-email template
        public static async Task<bool> SendEmailContactAsync(SendGridSettings sendGridSettings, string email, string companyName)
        {
            var toEmail = "support@rrida.com";

            var dynamicData = new Dictionary<string, object>
            {
                { "_company_name_", companyName },
                { "_email_", email }
            };

            return await EmailHelper.SendEmailAsync(
                sendGridSettings,
                toEmail,
                "New Contact Request \u2013 RRIDA",
                "ContactNotification",
                sendGridSettings.ContactTemplateId,
                dynamicData
            );
        }

        /// <summary>
        /// Mints the encrypted parameter an invitation link carries.
        ///
        /// Split out from the send so a caller can record the parameter as a
        /// single-use link BEFORE the email goes out. Nothing else about the
        /// value changed — it deserialises into ResetPasswordPayload exactly as
        /// before, and IsInvitation still marks it as an invitation so
        /// reset-password gives it the invitation window rather than the
        /// 30-minute forgot-password one.
        /// </summary>
        public static string BuildInvitationParam(string email, int userId)
        {
            var payload = new
            {
                Email = email,
                UserId = userId,
                RequestTime = DateTime.UtcNow,
                IsInvitation = true,
            };

            return EncryptObject(payload);
        }

        /// <summary>
        /// Sends the invitation email.
        ///
        /// <paramref name="invitationParam"/> is optional and additive: pass one
        /// that has already been recorded as a single-use link, or leave it null
        /// and this mints its own exactly as it always did. Callers that do not
        /// pass one keep their previous behaviour, link reuse included.
        /// </summary>
        public static async Task<bool> SendEmailToUserAsync(SendGridSettings sendGridSettings, string email, int userId, int profileId, string companyName, string? invitationParam = null)
        {
            var toEmail = email;
            var baseUrl = sendGridSettings.FrontendBaseUrl;

            var encrypted = string.IsNullOrWhiteSpace(invitationParam)
                ? BuildInvitationParam(email, userId)
                : invitationParam;

            var verifyLink = $"{baseUrl}/acceptInvite?data={encrypted}";

            var dynamicData = new Dictionary<string, object>
            {
                { "company_name", companyName },
                { "verify_link", verifyLink }
            };

            return await EmailHelper.SendEmailAsync(
                sendGridSettings,
                toEmail,
                "You're Invited \u2013 RRIDA",
                "UserInvitation",
                sendGridSettings.UserInviteTemplateId,
                dynamicData
            );
        }

        public static async Task<bool> SendCompanyNotificationEmailAsync(SendGridSettings sendGridSettings, string companyAdminEmail, string companyName, string unitName)
        {
            var dynamicData = new Dictionary<string, object>
            {
                { "company_name", companyName },
                { "unit_name", unitName }
            };

            return await EmailHelper.SendEmailAsync(
                sendGridSettings,
                companyAdminEmail,
                "New Unit Created \u2013 RRIDA",
                "CompanyNotification",
                sendGridSettings.CompanyNotificationTemplateId,
                dynamicData
            );
        }

        public static async Task<bool> SendReportNotificationEmailAsync(
            SendGridSettings sendGridSettings,
            string userEmail,
            long profileId,
            long unitId,
            long reportId,
            string unitName,
            DateTime fromDate,
            DateTime toDate)
        {
            // Format dates to readable format (e.g., "January 3, 2026")
            var formattedFromDate = fromDate.ToString("MMMM d, yyyy");
            var formattedToDate = toDate.ToString("MMMM d, yyyy");

            // Build report link in simple, decrypted form
            var baseUrl = sendGridSettings.FrontendBaseUrl;
            var reportLink = $"{baseUrl}/reports/{profileId}/{unitId}/{reportId}";

            // Build dynamic data dictionary for email template
            var dynamicData = new Dictionary<string, object>
            {
                { "unitName", unitName ?? "N/A" },
                { "fromDate", formattedFromDate },
                { "toDate", formattedToDate },
                { "reportLink", reportLink }
            };

            return await EmailHelper.SendEmailAsync(
                sendGridSettings,
                userEmail,
                "Report Available \u2013 RRIDA",
                "ReportNotification",
                sendGridSettings.CreateReportTemplateId,
                dynamicData
            );
        }

        // "Reports are waiting for you to review them" — to the reviewer list in
        // ReportApproval.ReviewerEmails.
        //
        // Deliberately a RAW html send rather than a SendGrid dynamic template:
        // the recipient list is configuration that changes without a deploy, the
        // body is a variable-length table of reports, and requiring a new
        // SendGrid template id would make this feature undeployable until
        // somebody built one in the SendGrid UI. SendRawHtmlEmailAsync works
        // identically on the SendGrid and Resend paths and takes all three
        // recipients in one message.
        // Each row carries the report's own id and the workbench run it came out
        // of, so a reviewer reading the mail can go straight to that run
        // (GET /api/prompt-management/workbench-run/{runId}/status) and to the
        // report itself without matching on company + team + dates. RunId is
        // null for a report queued by an earlier cycle whose mail never went out.
        public static async Task<bool> SendReportReviewRequestEmailAsync(
            SendGridSettings sendGridSettings,
            IEnumerable<string> reviewerEmails,
            int newReportCount,
            int pendingReportCount,
            string reviewLink,
            IEnumerable<(long ReportId, int? RunId, string CompanyName, string UnitName, DateTime FromDate, DateTime ToDate)> reports)
        {
            var rows = new StringBuilder();
            foreach (var r in reports ?? Enumerable.Empty<(long, int?, string, string, DateTime, DateTime)>())
            {
                rows.Append(
                    "<tr>" +
                    $"<td style=\"padding:8px; border-bottom:1px solid #eeeeee; font-size:14px; color:#333333;\">{r.ReportId}</td>" +
                    $"<td style=\"padding:8px; border-bottom:1px solid #eeeeee; font-size:14px; color:#333333;\">{(r.RunId.HasValue ? r.RunId.Value.ToString() : "&ndash;")}</td>" +
                    $"<td style=\"padding:8px; border-bottom:1px solid #eeeeee; font-size:14px; color:#333333;\">{HtmlEncode(r.CompanyName)}</td>" +
                    $"<td style=\"padding:8px; border-bottom:1px solid #eeeeee; font-size:14px; color:#333333;\">{HtmlEncode(r.UnitName)}</td>" +
                    $"<td style=\"padding:8px; border-bottom:1px solid #eeeeee; font-size:14px; color:#666666;\">{r.FromDate:MMMM d, yyyy} – {r.ToDate:MMMM d, yyyy}</td>" +
                    "</tr>");
            }

            var table = rows.Length == 0
                ? string.Empty
                : "<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:20px; border-collapse:collapse;\">" +
                  "<tr>" +
                  "<th align=\"left\" style=\"padding:8px; border-bottom:2px solid #dddddd; font-size:13px; color:#888888;\">Report ID</th>" +
                  "<th align=\"left\" style=\"padding:8px; border-bottom:2px solid #dddddd; font-size:13px; color:#888888;\">Run ID</th>" +
                  "<th align=\"left\" style=\"padding:8px; border-bottom:2px solid #dddddd; font-size:13px; color:#888888;\">Company</th>" +
                  "<th align=\"left\" style=\"padding:8px; border-bottom:2px solid #dddddd; font-size:13px; color:#888888;\">Team</th>" +
                  "<th align=\"left\" style=\"padding:8px; border-bottom:2px solid #dddddd; font-size:13px; color:#888888;\">Period</th>" +
                  "</tr>" + rows + "</table>";

            var html = EmailTemplateRenderer.Render("ReportReviewRequest", new Dictionary<string, object>
            {
                { "newReportCount", newReportCount },
                { "pendingReportCount", pendingReportCount },
                { "reviewLink", reviewLink },
                { "reportTable", table }
            });

            return await EmailHelper.SendRawHtmlEmailAsync(
                sendGridSettings,
                reviewerEmails,
                "Reports Awaiting Review – RRIDA",
                html);
        }

        // New-team onboarding nudges to the team's manager (day 2 and day 5
        // after the team was created).
        //
        // Raw html rather than a SendGrid dynamic template, for the same reason
        // as the reviewer email above: these are new messages and requiring two
        // new template ids to be created in the SendGrid UI first would block
        // the feature from being deployable. Both templates live in
        // Templates/TeamSetupDay2.html and Templates/TeamSetupDay5.html.
        //
        // The two sentences are passed in already composed rather than being
        // built from a count here, so the wording for "no responses yet" and
        // "12 responses" is decided in one place next to the numbers.
        public static Task<bool> SendTeamSetupDay2EmailAsync(
            SendGridSettings sendGridSettings,
            string managerEmail,
            string responseSentence,
            string encouragement,
            string scoresLink)
            => SendTeamSetupEmailAsync(
                sendGridSettings, managerEmail, "TeamSetupDay2",
                "Your team's first responses – RRIDA",
                responseSentence, encouragement, scoresLink);

        public static Task<bool> SendTeamSetupDay5EmailAsync(
            SendGridSettings sendGridSettings,
            string managerEmail,
            string responseSentence,
            string encouragement,
            string scoresLink)
            => SendTeamSetupEmailAsync(
                sendGridSettings, managerEmail, "TeamSetupDay5",
                "Your team's first week – RRIDA",
                responseSentence, encouragement, scoresLink);

        // "A team has been assigned to you" — sent the moment a manager is
        // assigned to a team, which is also the moment the day 2 / day 5
        // onboarding clock starts.
        public static async Task<bool> SendTeamAssignedEmailAsync(
            SendGridSettings sendGridSettings,
            string managerEmail,
            string assignmentSentence,
            string scoresLink)
        {
            var html = EmailTemplateRenderer.Render("TeamAssigned", new Dictionary<string, object>
            {
                { "assignmentSentence", HtmlEncode(assignmentSentence) },
                { "scoresLink", scoresLink }
            });

            return await EmailHelper.SendRawHtmlEmailAsync(
                sendGridSettings,
                new[] { managerEmail },
                "A team has been assigned to you – RRIDA",
                html);
        }

        // Generic manager alert: a headline sentence, a follow-up line and the
        // "see your scores" link. Used by the daily response monitor and the
        // Action Hub inactivity alert — both are the same shape, so they share
        // one template (Templates/TeamAlert.html) rather than one per alert.
        public static async Task<bool> SendTeamAlertEmailAsync(
            SendGridSettings sendGridSettings,
            string managerEmail,
            string subject,
            string alertSentence,
            string encouragement,
            string actionLink,
            string linkIntro,
            string linkLabel)
        {
            var html = EmailTemplateRenderer.Render("TeamAlert", new Dictionary<string, object>
            {
                { "alertSentence", HtmlEncode(alertSentence) },
                { "encouragement", HtmlEncode(encouragement) },
                { "actionLink", actionLink },
                { "linkIntro", HtmlEncode(linkIntro) },
                { "linkLabel", HtmlEncode(linkLabel) }
            });

            return await EmailHelper.SendRawHtmlEmailAsync(
                sendGridSettings, new[] { managerEmail }, subject, html);
        }

        private static async Task<bool> SendTeamSetupEmailAsync(
            SendGridSettings sendGridSettings,
            string managerEmail,
            string templateName,
            string subject,
            string responseSentence,
            string encouragement,
            string scoresLink)
        {
            var html = EmailTemplateRenderer.Render(templateName, new Dictionary<string, object>
            {
                { "responseSentence", HtmlEncode(responseSentence) },
                { "encouragement", HtmlEncode(encouragement) },
                { "scoresLink", scoresLink }
            });

            return await EmailHelper.SendRawHtmlEmailAsync(
                sendGridSettings,
                new[] { managerEmail },
                subject,
                html);
        }

        private static string HtmlEncode(string? value) =>
            System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

        public static (string Code, string ShortCode) GenerateCodes()
        {
            var code = Guid.NewGuid().ToString("D");
            var shortCode = code[..DataConstants.ShortCodeLength];

            return (code, shortCode);
        }

        public static class PasswordUtility
        {
            public static string Encrypt(string plainPassword)
            {
                return BCrypt.Net.BCrypt.HashPassword(plainPassword);
            }

            public static bool Verify(string plainPassword, string hashedPassword)
            {
                return BCrypt.Net.BCrypt.Verify(plainPassword, hashedPassword);
            }
        }
        public static string GenerateCsv(List<FeedbackDataResultDto> feedbackData)
        {
            var sb = new System.Text.StringBuilder();

            // Header row
            sb.AppendLine("FeedbackId,SubmissionDateTime,BusinessUnitId,BusinessUnitName,CompanyName,QuestionId,QuestionText,CategoryId,Category,ReasonId,ReasonText,SentimentTypeId,SentimentType,FreeText");

            // Data rows
            foreach (var item in feedbackData)
            {
                sb.AppendLine(string.Join(",", new string[]
                {
                    item.FeedbackId.ToString(),
                    item.SubmissionDateTime.ToString("o"), // ISO 8601 format
                    item.BusinessUnitId.ToString(),
                    EscapeCsv(item.BusinessUnitName ?? ""),
                    EscapeCsv(item.CompanyName ?? ""),
                    item.QuestionId?.ToString() ?? "",
                    EscapeCsv(item.QuestionText ?? ""),
                    item.CategoryId?.ToString() ?? "",
                    EscapeCsv(item.Category ?? ""),
                    item.ReasonId?.ToString() ?? "",
                    EscapeCsv(item.ReasonText ?? ""),
                    item.SentimentTypeId.ToString(),
                    EscapeCsv(item.SentimentType ?? ""),
                    EscapeCsv(item.FreeText ?? "")
                }));
            }

            return sb.ToString();
        }

        public static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            // Escape quotes & wrap in quotes if needed
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                value = value.Replace("\"", "\"\"");
                return $"\"{value}\"";
            }
            return value;
        }
        public static DateTime ToUtc(DateTime date)
        {
            if (date.Kind == DateTimeKind.Utc)
                return date;

            if (date.Kind == DateTimeKind.Local)
                return date.ToUniversalTime();

            // If Unspecified, assume Local or given timezone
            return DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime();
        }
    }
}
