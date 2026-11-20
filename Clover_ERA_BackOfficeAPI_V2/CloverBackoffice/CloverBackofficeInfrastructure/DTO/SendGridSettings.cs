using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class SendGridSettings
    {
        public string ApiKey { get; set; }
        public string FromEmail { get; set; }
        public string FromName { get; set; }
        public string TemplateId { get; set; }
        public string UserInviteTemplateId { get; set; }
        public string ResetPasswordTemplateId { get; set; }
        public string CompanyNotificationTemplateId { get; set; }
        public string ContactTemplateId { get; set; }
        public string FrontendBaseUrl { get; set; }
        public string FeedbackAppBaseUrl { get; set; }
        public string CreateReportTemplateId { get; set; }
        public bool UseSendGrid { get; set; } = true;
        public string ResendApiKey { get; set; }

    }
}
