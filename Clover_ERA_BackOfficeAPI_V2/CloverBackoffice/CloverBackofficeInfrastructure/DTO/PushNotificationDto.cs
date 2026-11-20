using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// DTO for subscribing to push notifications
    /// </summary>
    public class SubscribePushNotificationDto
    {
        [Required(ErrorMessage = "OneSignal Player ID is required")]
        public string OneSignalPlayerId { get; set; }
    }

    /// <summary>
    /// DTO for updating push notification preferences
    /// </summary>
    public class UpdatePushNotificationPreferenceDto
    {
        [Required(ErrorMessage = "IsEnabled is required")]
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// DTO for push notification preference response
    /// </summary>
    public class PushNotificationPreferenceDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string OneSignalPlayerId { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// DTO for OneSignal notification request
    /// </summary>
    public class OneSignalNotificationDto
    {
        [JsonPropertyName("app_id")]
        public string AppId { get; set; }

        [JsonPropertyName("include_player_ids")]
        public string[] IncludePlayerIds { get; set; }

        [JsonPropertyName("contents")]
        public NotificationContent Contents { get; set; }

        [JsonPropertyName("headings")]
        public NotificationContent Headings { get; set; }

        [JsonPropertyName("data")]
        public object Data { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        /// Icon URL for Chrome/Edge notifications (recommended 256x256 PNG)
        /// </summary>
        [JsonPropertyName("chrome_web_icon")]
        public string ChromeWebIcon { get; set; }

        /// <summary>
        /// Icon URL for Firefox notifications
        /// </summary>
        [JsonPropertyName("firefox_icon")]
        public string FirefoxIcon { get; set; }

        /// <summary>
        /// Small badge icon for Chrome (optional, monochrome recommended)
        /// </summary>
        [JsonPropertyName("chrome_web_badge")]
        public string ChromeWebBadge { get; set; }
    }

    /// <summary>
    /// DTO for notification content (supports multiple languages)
    /// </summary>
    public class NotificationContent
    {
        [JsonPropertyName("en")]
        public string En { get; set; }
    }

    /// <summary>
    /// DTO for report notification data
    /// </summary>
    public class ReportNotificationDataDto
    {
        public long ReportId { get; set; }
        public long UnitId { get; set; }
        public long ProfileId { get; set; }
        public string ReportTitle { get; set; }
        public string ReportUrl { get; set; }
    }
}
