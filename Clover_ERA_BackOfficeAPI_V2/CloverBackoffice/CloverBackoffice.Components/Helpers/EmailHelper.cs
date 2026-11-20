using CloverBackoffice.Infrastructure.DTO;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SendGridEmailAddress = SendGrid.Helpers.Mail.EmailAddress;

namespace CloverBackoffice.Components.Helpers
{
    public static class EmailHelper
    {
        private static readonly HttpClient _httpClient = new();

        private static readonly JsonSerializerOptions ResendPayloadOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Provider-agnostic RAW html email to one or more recipients. Unlike the
        // template methods above this carries an arbitrary html body (used by the
        // snapshot troubleshooting log), and works for both providers:
        //   - SendGrid: single message, multiple To addresses.
        //   - Resend  : "to" accepts the full recipient array.
        // An optional calendar invite is carried as a text/calendar REQUEST part rather
        // than as a downloadable file, so the mail client shows the event with its
        // accept / decline controls instead of an attachment:
        //   - SendGrid: an extra content part on the same message.
        //   - Resend  : has no alternative-content field, so the part is declared with
        //               the text/calendar content type, which clients render the same way.
        // Returns false (without throwing) when no usable recipient is supplied.
        public static async Task<bool> SendRawHtmlEmailAsync(
            SendGridSettings settings,
            IEnumerable<string> toEmails,
            string subject,
            string htmlBody,
            EmailCalendarInvite? calendarInvite = null)
        {
            var invite = string.IsNullOrWhiteSpace(calendarInvite?.Calendar) ? null : calendarInvite;

            var recipients = (toEmails ?? Enumerable.Empty<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (recipients.Count == 0)
                return false;

            if (settings.UseSendGrid)
            {
                var client = new SendGridClient(settings.ApiKey);
                var from = new SendGridEmailAddress(settings.FromEmail, settings.FromName);
                var tos = recipients.Select(r => new SendGridEmailAddress(r)).ToList();

                var msg = MailHelper.CreateSingleEmailToMultipleRecipients(
                    from, tos, subject, plainTextContent: null, htmlContent: htmlBody);

                if (invite != null)
                {
                    // Sits alongside the html part on the same message, so the invite is the
                    // mail rather than something hanging off it.
                    msg.AddContent(invite.ContentType, invite.Calendar);

                    if (invite.AttachFile)
                    {
                        msg.AddAttachment(
                            invite.AttachmentFileName,
                            System.Convert.ToBase64String(Encoding.UTF8.GetBytes(AttachmentCalendarOf(invite))),
                            "text/calendar");
                    }
                }

                var response = await client.SendEmailAsync(msg);
                return response.IsSuccessStatusCode;
            }
            else
            {
                // The invitation part (REQUEST) is what the mail client renders with accept /
                // decline; the optional downloadable file (PUBLISH) rides alongside it.
                var attachments = invite == null
                    ? null
                    : new List<object>
                    {
                        new
                        {
                            filename = invite.FileName,
                            content = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(invite.Calendar)),
                            content_type = invite.ContentType
                        }
                    };

                if (invite != null && invite.AttachFile && !string.IsNullOrWhiteSpace(invite.AttachmentCalendar))
                {
                    attachments!.Add(new
                    {
                        filename = invite.AttachmentFileName,
                        content = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(invite.AttachmentCalendar)),
                        content_type = "text/calendar; charset=utf-8"
                    });
                }

                var payload = new
                {
                    from = $"{settings.FromName} <{settings.FromEmail}>",
                    to = recipients.ToArray(),
                    subject,
                    html = htmlBody,
                    attachments
                };

                // Resend rejects a null "attachments" member, so it is dropped when absent.
                var json = JsonSerializer.Serialize(payload, ResendPayloadOptions);
                using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.resend.com/emails")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ResendApiKey);

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
        }

        // The downloadable .ics: the separate editable event when one was supplied, otherwise
        // the invitation itself (the behaviour before the two were split).
        private static string AttachmentCalendarOf(EmailCalendarInvite invite) =>
            string.IsNullOrWhiteSpace(invite.AttachmentCalendar) ? invite.Calendar : invite.AttachmentCalendar;

        // Editable-event variant of SendRawHtmlEmailAsync, kept separate so the meeting-request
        // path above stays unchanged. The calendar travels ONLY as a .ics attachment
        // (a METHOD:PUBLISH event with no ORGANIZER / ATTENDEE) and there is no inline
        // text/calendar part. An inline REQUEST part is filed by Outlook / Exchange as a meeting
        // owned by the sender, so the recipient cannot change its date or time. An attached
        // plain event is added via "Add to calendar" and belongs to the recipient, so date,
        // time and reminder stay editable. It also has no organizer, so nothing is sent back
        // to the From address.
        // Returns false (without throwing) when no usable recipient is supplied.
        public static async Task<bool> SendHtmlEmailWithCalendarAttachmentAsync(
            SendGridSettings settings,
            IEnumerable<string> toEmails,
            string subject,
            string htmlBody,
            string calendar,
            string fileName,
            string contentType)
        {
            var recipients = (toEmails ?? Enumerable.Empty<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (recipients.Count == 0)
                return false;

            var content = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(calendar ?? string.Empty));

            if (settings.UseSendGrid)
            {
                var client = new SendGridClient(settings.ApiKey);
                var from = new SendGridEmailAddress(settings.FromEmail, settings.FromName);
                var tos = recipients.Select(r => new SendGridEmailAddress(r)).ToList();

                var msg = MailHelper.CreateSingleEmailToMultipleRecipients(
                    from, tos, subject, plainTextContent: null, htmlContent: htmlBody);

                msg.AddAttachment(fileName, content, contentType, "attachment");

                var response = await client.SendEmailAsync(msg);
                return response.IsSuccessStatusCode;
            }
            else
            {
                var payload = new
                {
                    from = $"{settings.FromName} <{settings.FromEmail}>",
                    to = recipients.ToArray(),
                    subject,
                    html = htmlBody,
                    attachments = new List<object>
                    {
                        new { filename = fileName, content, content_type = contentType }
                    }
                };

                var json = JsonSerializer.Serialize(payload, ResendPayloadOptions);
                using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.resend.com/emails")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ResendApiKey);

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
        }

        public static async Task<bool> SendEmailAsync(
            SendGridSettings settings,
            string toEmail,
            string subject,
            string templateName,
            string templateId,
            Dictionary<string, object> dynamicData)
        {
            if (settings.UseSendGrid)
            {
                return await SendDynamicTemplateEmailAsync(
                    settings.ApiKey,
                    settings.FromEmail,
                    settings.FromName,
                    toEmail,
                    templateId,
                    dynamicData);
            }
            else
            {
                var html = EmailTemplateRenderer.Render(templateName, dynamicData);
                return await SendResendEmailAsync(
                    settings.ResendApiKey,
                    settings.FromEmail,
                    settings.FromName,
                    toEmail,
                    subject,
                    html);
            }
        }

        public static async Task<bool> SendDynamicTemplateEmailAsync(
            string apiKey,
            string fromEmail,
            string fromName,
            string toEmail,
            string templateId,
            Dictionary<string, object> dynamicData)
        {
            var client = new SendGridClient(apiKey);
            var from = new SendGridEmailAddress(fromEmail, fromName);
            var to = new SendGridEmailAddress(toEmail);

            var msg = MailHelper.CreateSingleTemplateEmail(from, to, templateId, dynamicData);
            var response = await client.SendEmailAsync(msg);

            return response.IsSuccessStatusCode;
        }

        private static async Task<bool> SendResendEmailAsync(
            string apiKey,
            string fromEmail,
            string fromName,
            string toEmail,
            string subject,
            string htmlBody)
        {
            var payload = new
            {
                from = $"{fromName} <{fromEmail}>",
                to = new[] { toEmail },
                subject,
                html = htmlBody
            };

            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.resend.com/emails")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
    }
}
