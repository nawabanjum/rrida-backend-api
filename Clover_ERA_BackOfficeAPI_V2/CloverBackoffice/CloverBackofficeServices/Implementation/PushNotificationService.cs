using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class PushNotificationService : IPushNotificationService
    {
        private readonly IPushNotificationRepository _repository;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        private readonly string _oneSignalAppId;
        private readonly string _oneSignalApiKey;
        private readonly string _oneSignalApiUrl;
        private readonly string _notificationIconUrl;

        public PushNotificationService(
            IPushNotificationRepository repository,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _repository = repository;
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient();

            // Load OneSignal configuration from appsettings
            _oneSignalAppId = _configuration["OneSignal:AppId"];
            _oneSignalApiKey = _configuration["OneSignal:ApiKey"];
            _oneSignalApiUrl = _configuration["OneSignal:RestApiUrl"] ?? "https://onesignal.com/api/v1";
            _notificationIconUrl = _configuration["OneSignal:NotificationIconUrl"];
        }


        public async Task<(bool IsSuccess, string Message)> SendReportNotificationAsync(
            int teamManagerId,
            long reportId,
            long unitId,
            long profileId,
            string reportTitle)
        {
            try
            {
                // Check if user has push notifications enabled
                var isEnabled = await _repository.IsEnabledForUserAsync(teamManagerId);
                if (!isEnabled)
                {
                    return (true, "User has push notifications disabled, skipping notification");
                }

                // Get user's player IDs
                var playerIds = await _repository.GetEnabledPlayerIdsByUserIdsAsync(new List<int> { teamManagerId });

                if (!playerIds.Any())
                {
                    return (false, "No active push notification subscriptions found for this user");
                }

                // Get frontend base URL from configuration
                var frontendBaseUrl = _configuration["SendGrid:FrontendBaseUrl"];
                var reportUrl = $"{frontendBaseUrl}/dashboard/{profileId}/{unitId}/report/{reportId}";

                // Prepare notification data
                var notificationData = new ReportNotificationDataDto
                {
                    ReportId = reportId,
                    UnitId = unitId,
                    ProfileId = profileId,
                    ReportTitle = reportTitle,
                    ReportUrl = reportUrl
                };

                // Send OneSignal notification
                var result = await SendOneSignalNotificationAsync(
                    playerIds.ToArray(),
                    "New Report Available",
                    $"Your bi-weekly report '{reportTitle}' is now available",
                    notificationData
                );

                return result;
            }
            catch (Exception ex)
            {
                return (false, $"Failed to send report notification: {ex.Message}");
            }
        }

        public async Task<(bool IsSuccess, string Message)> SendNotificationToUsersAsync(
            List<int> userIds,
            string title,
            string message,
            object data = null)
        {
            try
            {
                if (userIds == null || !userIds.Any())
                {
                    return (false, "No user IDs provided");
                }

                // Get all enabled player IDs for these users
                var playerIds = await _repository.GetEnabledPlayerIdsByUserIdsAsync(userIds);

                if (!playerIds.Any())
                {
                    return (false, "No active push notification subscriptions found for these users");
                }

                // Send OneSignal notification
                var result = await SendOneSignalNotificationAsync(
                    playerIds.ToArray(),
                    title,
                    message,
                    data
                );

                return result;
            }
            catch (Exception ex)
            {
                return (false, $"Failed to send notifications: {ex.Message}");
            }
        }

        private async Task<(bool IsSuccess, string Message)> SendOneSignalNotificationAsync(string[] playerIds, string title, string message, object data = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_oneSignalAppId) || string.IsNullOrWhiteSpace(_oneSignalApiKey))
                {
                    return (false, "OneSignal configuration is missing. Please configure OneSignal AppId and ApiKey in appsettings.json");
                }

                if (playerIds == null || playerIds.Length == 0)
                {
                    return (false, "No player IDs provided for the notification.");
                }

                var notification = new OneSignalNotificationDto
                {
                    AppId = _oneSignalAppId,
                    IncludePlayerIds = playerIds,
                    Headings = new NotificationContent { En = title },
                    Contents = new NotificationContent { En = message },
                    Data = data,
                    ChromeWebIcon = _notificationIconUrl,
                    FirefoxIcon = _notificationIconUrl
                };

                var jsonContent = JsonSerializer.Serialize(notification, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_oneSignalApiUrl}/notifications")
                {
                    Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
                };

                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", _oneSignalApiKey);

                try
                {
                    var response = await _httpClient.SendAsync(request);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        return (true, "Notification sent successfully");
                    }
                    else
                    {
                        return (false, $"OneSignal API error: {response.StatusCode} - {responseContent}");
                    }
                }
                catch (HttpRequestException httpEx)
                {
                    return (false, $"HTTP request failed: {httpEx.Message}");
                }
                catch (TaskCanceledException taskEx)
                {
                    return (false, $"Request timed out: {taskEx.Message}");
                }
                catch (Exception innerEx)
                {
                    return (false, $"Unexpected error sending notification: {innerEx.Message}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Failed to prepare OneSignal notification: {ex.Message}");
            }
        }
        public async Task<(bool IsSuccess, string Message, UserSystemDetailsDto Data)> SaveSystemDetailsAsync(
            int userId,
            string ipAddress,
            string userAgent,
            string? oneSignalPlayerId = null)
        {
            try
            {
                // Parse user agent to extract system details
                var (browserName, browserVersion) = ParseBrowser(userAgent);
                var (osName, osVersion) = ParseOperatingSystem(userAgent);
                var deviceType = ParseDeviceType(userAgent);
                var architecture = ParseArchitecture(userAgent);
                var deviceBrand = ParseDeviceBrand(userAgent);

                // Try to extract device model from user agent (basic detection)
                string? deviceModel = ExtractDeviceModelFromUserAgent(userAgent);
                var latestRecord = await _repository.GetLatestUserSystemDetailsAsync(userId);
                string savedOneSignalPlayerId = latestRecord != null && !string.IsNullOrEmpty(latestRecord.OneSignalPlayerId) ? latestRecord.OneSignalPlayerId : null;
                var systemDetails = new UserSystemDetails
                {
                    UserId = userId,
                    IPAddress = ipAddress,
                    DeviceType = deviceType,
                    DeviceBrand = deviceBrand,
                    DeviceModel = deviceModel,
                    MachineName = GetMachineNameFromEnvironment(), // Get from server environment
                    OperatingSystem = osName,
                    OSVersion = osVersion,
                    Architecture = architecture,
                    BrowserName = browserName,
                    BrowserVersion = browserVersion,
                    UserAgent = userAgent,
                    OneSignalPlayerId = oneSignalPlayerId ?? savedOneSignalPlayerId,
                    LoginTime = DateTime.UtcNow
                };

                var result = await _repository.SaveSystemDetailsAsync(systemDetails);

                var resultDto = new UserSystemDetailsDto
                {
                    Id = result.Id,
                    UserId = result.UserId,
                    IPAddress = result.IPAddress,
                    DeviceType = result.DeviceType,
                    DeviceBrand = result.DeviceBrand,
                    DeviceModel = result.DeviceModel,
                    MachineName = result.MachineName,
                    OperatingSystem = result.OperatingSystem,
                    OSVersion = result.OSVersion,
                    Architecture = result.Architecture,
                    BrowserName = result.BrowserName,
                    BrowserVersion = result.BrowserVersion,
                    UserAgent = result.UserAgent,
                    OneSignalPlayerId = result.OneSignalPlayerId,
                    LoginTime = result.LoginTime,
                    CreatedAt = result.CreatedAt
                };

                return (true, "System details saved successfully", resultDto);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to save system details: {ex.Message}", null);
            }
        }

        #region User Agent Parsing Helpers
        private string? ExtractDeviceModelFromUserAgent(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return null;

            // Simple device model detection from user agent
            if (userAgent.Contains("iPhone"))
            {
                // Try to extract iPhone model
                if (userAgent.Contains("iPhone13")) return "iPhone 13";
                if (userAgent.Contains("iPhone12")) return "iPhone 12";
                if (userAgent.Contains("iPhone11")) return "iPhone 11";
                return "iPhone";
            }

            if (userAgent.Contains("iPad"))
            {
                // Try to extract iPad model
                if (userAgent.Contains("iPad Pro")) return "iPad Pro";
                if (userAgent.Contains("iPad Air")) return "iPad Air";
                if (userAgent.Contains("iPad Mini")) return "iPad Mini";
                return "iPad";
            }

            if (userAgent.Contains("Samsung"))
            {
                if (userAgent.Contains("Galaxy S22")) return "Galaxy S22";
                if (userAgent.Contains("Galaxy S21")) return "Galaxy S21";
                if (userAgent.Contains("Galaxy Note")) return "Galaxy Note";
                return "Samsung Device";
            }

            // Add more device detection as needed

            return null;
        }

        private string? GetMachineNameFromEnvironment()
        {
            try
            {
                // Get machine name from server environment
                return Environment.MachineName;
            }
            catch
            {
                return null;
            }
        }
        private (string Name, string Version) ParseBrowser(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return ("Unknown", null);

            // Order matters - check more specific browsers first
            if (userAgent.Contains("Edg/"))
            {
                var version = ExtractVersion(userAgent, "Edg/");
                return ("Microsoft Edge", version);
            }
            if (userAgent.Contains("Edge/"))
            {
                var version = ExtractVersion(userAgent, "Edge/");
                return ("Microsoft Edge", version);
            }
            if (userAgent.Contains("OPR/") || userAgent.Contains("Opera/"))
            {
                var version = ExtractVersion(userAgent, "OPR/") ?? ExtractVersion(userAgent, "Opera/");
                return ("Opera", version);
            }
            if (userAgent.Contains("Chrome/") && !userAgent.Contains("Chromium/"))
            {
                var version = ExtractVersion(userAgent, "Chrome/");
                return ("Google Chrome", version);
            }
            if (userAgent.Contains("Firefox/"))
            {
                var version = ExtractVersion(userAgent, "Firefox/");
                return ("Mozilla Firefox", version);
            }
            if (userAgent.Contains("Safari/") && !userAgent.Contains("Chrome/"))
            {
                var version = ExtractVersion(userAgent, "Version/");
                return ("Safari", version);
            }
            if (userAgent.Contains("MSIE") || userAgent.Contains("Trident/"))
            {
                var version = ExtractVersion(userAgent, "MSIE ") ?? ExtractVersion(userAgent, "rv:");
                return ("Internet Explorer", version);
            }

            return ("Unknown", null);
        }

        private (string Name, string Version) ParseOperatingSystem(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return ("Unknown", null);

            if (userAgent.Contains("Windows NT 10.0"))
                return ("Windows", "10/11");
            if (userAgent.Contains("Windows NT 6.3"))
                return ("Windows", "8.1");
            if (userAgent.Contains("Windows NT 6.2"))
                return ("Windows", "8");
            if (userAgent.Contains("Windows NT 6.1"))
                return ("Windows", "7");
            if (userAgent.Contains("Windows NT 6.0"))
                return ("Windows", "Vista");
            if (userAgent.Contains("Windows NT 5.1"))
                return ("Windows", "XP");
            if (userAgent.Contains("Windows"))
                return ("Windows", null);

            if (userAgent.Contains("Mac OS X"))
            {
                var version = ExtractMacVersion(userAgent);
                return ("macOS", version);
            }

            if (userAgent.Contains("iPhone"))
                return ("iOS", ExtractIOSVersion(userAgent));
            if (userAgent.Contains("iPad"))
                return ("iPadOS", ExtractIOSVersion(userAgent));

            if (userAgent.Contains("Android"))
            {
                var version = ExtractVersion(userAgent, "Android ");
                return ("Android", version);
            }

            if (userAgent.Contains("Linux"))
                return ("Linux", null);

            if (userAgent.Contains("CrOS"))
                return ("Chrome OS", null);

            return ("Unknown", null);
        }

        private string ParseDeviceType(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return "Unknown";

            if (userAgent.Contains("Mobile") || userAgent.Contains("iPhone") || userAgent.Contains("Android") && !userAgent.Contains("Tablet"))
                return "Mobile";

            if (userAgent.Contains("Tablet") || userAgent.Contains("iPad"))
                return "Tablet";

            return "Desktop";
        }

        private string ParseArchitecture(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return null;

            if (userAgent.Contains("x64") || userAgent.Contains("x86_64") || userAgent.Contains("Win64") || userAgent.Contains("WOW64") || userAgent.Contains("amd64"))
                return "x64";

            if (userAgent.Contains("arm64") || userAgent.Contains("aarch64"))
                return "ARM64";

            if (userAgent.Contains("arm"))
                return "ARM";

            if (userAgent.Contains("x86") || userAgent.Contains("i686") || userAgent.Contains("i386"))
                return "x86";

            return null;
        }

        private string ParseDeviceBrand(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return null;

            if (userAgent.Contains("iPhone") || userAgent.Contains("iPad") || userAgent.Contains("Macintosh"))
                return "Apple";

            if (userAgent.Contains("Samsung"))
                return "Samsung";

            if (userAgent.Contains("Huawei"))
                return "Huawei";

            if (userAgent.Contains("Xiaomi") || userAgent.Contains("Mi ") || userAgent.Contains("Redmi"))
                return "Xiaomi";

            if (userAgent.Contains("OPPO"))
                return "OPPO";

            if (userAgent.Contains("vivo"))
                return "Vivo";

            if (userAgent.Contains("OnePlus"))
                return "OnePlus";

            if (userAgent.Contains("Pixel"))
                return "Google";

            if (userAgent.Contains("LG"))
                return "LG";

            if (userAgent.Contains("Sony"))
                return "Sony";

            if (userAgent.Contains("Nokia"))
                return "Nokia";

            if (userAgent.Contains("Motorola"))
                return "Motorola";

            return null;
        }

        private string ExtractVersion(string userAgent, string prefix)
        {
            try
            {
                var startIndex = userAgent.IndexOf(prefix);
                if (startIndex == -1) return null;

                startIndex += prefix.Length;
                var endIndex = startIndex;

                while (endIndex < userAgent.Length && (char.IsDigit(userAgent[endIndex]) || userAgent[endIndex] == '.'))
                {
                    endIndex++;
                }

                if (endIndex > startIndex)
                {
                    return userAgent.Substring(startIndex, endIndex - startIndex);
                }
            }
            catch { }

            return null;
        }

        private string ExtractMacVersion(string userAgent)
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(userAgent, @"Mac OS X (\d+[_\.]\d+([_\.]\d+)?)");
                if (match.Success)
                {
                    return match.Groups[1].Value.Replace("_", ".");
                }
            }
            catch { }

            return null;
        }

        private string ExtractIOSVersion(string userAgent)
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(userAgent, @"OS (\d+[_\.]\d+([_\.]\d+)?)");
                if (match.Success)
                {
                    return match.Groups[1].Value.Replace("_", ".");
                }
            }
            catch { }

            return null;
        }

        #endregion


        // -------------------------------------------------------------------------------------
        public async Task<MatchResultDto> CheckForMatchingSystemDetailsAsync(SystemDetailsMatchRequestDto matchRequest)
        {
            try
            {
                if (matchRequest.UserId <= 0)
                {
                    return new MatchResultDto
                    {
                        IsMatch = false,
                        MatchPercentage = 0,
                        MatchedFields = 0,
                        TotalFieldsWithValues = 0,
                        Message = "Valid UserId is required"
                    };
                }

                // Get the latest record for the user
                var latestRecord = await _repository.GetLatestUserSystemDetailsAsync(matchRequest.UserId);

                if (latestRecord == null)
                {
                    return new MatchResultDto
                    {
                        IsMatch = false,
                        MatchPercentage = 0,
                        MatchedFields = 0,
                        TotalFieldsWithValues = 0,
                        Message = "No previous login records found"
                    };
                }

                // Parse OS and Browser from UserAgent if provided in request
                if (!string.IsNullOrWhiteSpace(matchRequest.UserAgent))
                {
                    var parsedDetails = ParseUserAgentForMatching(matchRequest.UserAgent);

                    if (string.IsNullOrWhiteSpace(matchRequest.BrowserName))
                        matchRequest.BrowserName = parsedDetails.BrowserName;

                    if (string.IsNullOrWhiteSpace(matchRequest.BrowserVersion))
                        matchRequest.BrowserVersion = parsedDetails.BrowserVersion;

                    if (string.IsNullOrWhiteSpace(matchRequest.OSVersion))
                        matchRequest.OSVersion = parsedDetails.OSVersion;
                }

                // Check if it's a match based on the rules
                var matchAnalysis = AnalyzeMatch(latestRecord, matchRequest);

                var result = new MatchResultDto
                {
                    IsMatch = matchAnalysis.IsMatch,
                    MatchPercentage = matchAnalysis.MatchPercentage,
                    MatchedFields = matchAnalysis.MatchedFields,
                    TotalFieldsWithValues = matchAnalysis.TotalFieldsWithValues,
                };

                if (matchAnalysis.IsMatch)
                {
                    result.MatchingRecord = MapToUserSystemDetailsDto(latestRecord);
                    result.OneSignalPlayerId = latestRecord.OneSignalPlayerId;
                }

                return result;
            }
            catch (Exception ex)
            {
                return new MatchResultDto
                {
                    IsMatch = false,
                    MatchPercentage = 0,
                    MatchedFields = 0,
                    TotalFieldsWithValues = 0,
                    Message = $"Error checking match: {ex.Message}"
                };
            }
        }

        public async Task<bool> SendNotificationIfSystemDetailsMatchAsync(SystemDetailsMatchRequestDto matchRequest)
        {
            try
            {
                var matchResult = await CheckForMatchingSystemDetailsAsync(matchRequest);

                if (matchResult.IsMatch && !string.IsNullOrWhiteSpace(matchResult.OneSignalPlayerId))
                {
                    // Send OneSignal notification
                    var notificationSent = await SendOneSignalNotificationForMatchAsync(
                        matchResult.OneSignalPlayerId,
                        matchRequest,
                        matchResult.MatchingRecord);

                    if (notificationSent)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        // -------------------------------------------------------------------------------------
        // PRIVATE HELPER METHODS FOR MATCHING LOGIC
        // -------------------------------------------------------------------------------------

        private (bool IsMatch, double MatchPercentage, int MatchedFields, int TotalFieldsWithValues)
            AnalyzeMatch(UserSystemDetails record, SystemDetailsMatchRequestDto request)
        {
            // Rule 1: OneSignalPlayerId check
            // If query has OneSignalPlayerId, it must match the record's OneSignalPlayerId
            if (!string.IsNullOrWhiteSpace(request.OneSignalPlayerId) &&
                !string.Equals(record.OneSignalPlayerId, request.OneSignalPlayerId, StringComparison.OrdinalIgnoreCase))
            {
                return (false, 0, 0, 0);
            }

            // Define the fields to match
            var fieldsToMatch = new List<(string FieldName, string? RecordValue, string? RequestValue)>
            {
                ("IPAddress", record.IPAddress, request.IPAddress),
                ("DeviceModel", record.DeviceModel, request.DeviceModel),
                ("MachineName", record.MachineName, request.MachineName),
                ("OSVersion", record.OSVersion, request.OSVersion),
                ("BrowserName", record.BrowserName, request.BrowserName),
                ("BrowserVersion", record.BrowserVersion, request.BrowserVersion),
                ("UserAgent", record.UserAgent, request.UserAgent),
                ("OneSignalPlayerId", record.OneSignalPlayerId, request.OneSignalPlayerId)
            };

            // Count matches
            int totalFieldsWithValues = 0;
            int matches = 0;

            foreach (var field in fieldsToMatch)
            {
                // Only consider fields where request has a value
                if (!string.IsNullOrWhiteSpace(field.RequestValue))
                {
                    totalFieldsWithValues++;

                    // Compare values (case-insensitive for strings, handle nulls)
                    bool isMatch = string.Equals(
                        field.RecordValue?.Trim(),
                        field.RequestValue?.Trim(),
                        StringComparison.OrdinalIgnoreCase);

                    if (isMatch)
                    {
                        matches++;
                    }
                }
            }

            // If no fields have values in request, it's not a match
            if (totalFieldsWithValues == 0)
            {
                return (false, 0, 0, 0);
            }

            // Calculate match percentage
            double matchPercentage = (double)matches / totalFieldsWithValues * 100;
            bool isPartialMatch = matchPercentage >= 50;

            return (isPartialMatch, matchPercentage, matches, totalFieldsWithValues);
        }

        private (string? BrowserName, string? BrowserVersion, string? OSVersion, string? DeviceType, string? DeviceBrand)
       ParseUserAgentForMatching(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return (null, null, null, null, null);

            var browserInfo = ParseBrowser(userAgent);
            var osInfo = ParseOperatingSystem(userAgent);
            var deviceType = ParseDeviceType(userAgent);
            var deviceBrand = ParseDeviceBrand(userAgent);

            return (browserInfo.Name, browserInfo.Version, osInfo.Version, deviceType, deviceBrand);
        }

        private async Task<bool> SendOneSignalNotificationForMatchAsync(
            string playerId,
            SystemDetailsMatchRequestDto request,
            UserSystemDetailsDto? matchingRecord)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_oneSignalAppId) || string.IsNullOrWhiteSpace(_oneSignalApiKey))
                {
                    return false;
                }

                var notification = new OneSignalNotificationDto
                {
                    AppId = _oneSignalAppId,
                    IncludePlayerIds = new[] { playerId },
                    Headings = new NotificationContent { En = "New Login Detected" },
                    Contents = new NotificationContent { En = "A new login was detected from a different device or location." },
                    ChromeWebIcon = _notificationIconUrl,
                    FirefoxIcon = _notificationIconUrl,
                    Data = new
                    {
                        userId = request.UserId,
                        eventType = "new_login",
                        timestamp = DateTime.UtcNow,
                        currentLogin = new
                        {
                            ipAddress = request.IPAddress,
                            deviceModel = request.DeviceModel,
                            browser = request.BrowserName
                        },
                        previousLogin = matchingRecord != null ? new
                        {
                            ipAddress = matchingRecord.IPAddress,
                            deviceModel = matchingRecord.DeviceModel,
                            browser = matchingRecord.BrowserName,
                            loginTime = matchingRecord.LoginTime
                        } : null
                    }
                };

                var jsonContent = JsonSerializer.Serialize(notification, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_oneSignalApiUrl}/notifications")
                {
                    Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
                };

                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", _oneSignalApiKey);

                var response = await _httpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private UserSystemDetailsDto MapToUserSystemDetailsDto(UserSystemDetails entity)
        {
            if (entity == null)
                return null;

            return new UserSystemDetailsDto
            {
                Id = entity.Id,
                UserId = entity.UserId,
                IPAddress = entity.IPAddress,
                DeviceType = entity.DeviceType,
                DeviceBrand = entity.DeviceBrand,
                DeviceModel = entity.DeviceModel,
                MachineName = entity.MachineName,
                OperatingSystem = entity.OperatingSystem,
                OSVersion = entity.OSVersion,
                Architecture = entity.Architecture,
                BrowserName = entity.BrowserName,
                BrowserVersion = entity.BrowserVersion,
                UserAgent = entity.UserAgent,
                OneSignalPlayerId = entity.OneSignalPlayerId,
                LoginTime = entity.LoginTime,
                CreatedAt = entity.CreatedAt
            };
        }
        // In your PushNotificationService class, add this method:
        public async Task<(bool IsSuccess, string Message, UserSystemDetailsDto Data)> UpdateLatestRecordWithPlayerIdAsync(
            int userId,
            string oneSignalPlayerId)
        {
            try
            {
                if (userId <= 0)
                {
                    return (false, "Valid UserId is required", null);
                }

                if (string.IsNullOrWhiteSpace(oneSignalPlayerId))
                {
                    return (false, "OneSignal Player ID is required", null);
                }

                // Get the latest record for the user
                var latestRecord = await _repository.GetLatestUserSystemDetailsAsync(userId);

                if (latestRecord == null)
                {
                    return (false, $"No system details found for user {userId}", null);
                }

                // Create a copy of the latest record with the new PlayerId
                var updatedRecord = new UserSystemDetails
                {
                    UserId = latestRecord.UserId,
                    IPAddress = latestRecord.IPAddress,
                    DeviceType = latestRecord.DeviceType,
                    DeviceBrand = latestRecord.DeviceBrand,
                    DeviceModel = latestRecord.DeviceModel,
                    MachineName = latestRecord.MachineName,
                    OperatingSystem = latestRecord.OperatingSystem,
                    OSVersion = latestRecord.OSVersion,
                    Architecture = latestRecord.Architecture,
                    BrowserName = latestRecord.BrowserName,
                    BrowserVersion = latestRecord.BrowserVersion,
                    UserAgent = latestRecord.UserAgent,
                    OneSignalPlayerId = oneSignalPlayerId,
                    LoginTime = DateTime.UtcNow
                };

                // Save the new record with updated PlayerId
                var result = await _repository.SaveSystemDetailsAsync(updatedRecord);

                var resultDto = new UserSystemDetailsDto
                {
                    Id = result.Id,
                    UserId = result.UserId,
                    IPAddress = result.IPAddress,
                    DeviceType = result.DeviceType,
                    DeviceBrand = result.DeviceBrand,
                    DeviceModel = result.DeviceModel,
                    MachineName = result.MachineName,
                    OperatingSystem = result.OperatingSystem,
                    OSVersion = result.OSVersion,
                    Architecture = result.Architecture,
                    BrowserName = result.BrowserName,
                    BrowserVersion = result.BrowserVersion,
                    UserAgent = result.UserAgent,
                    OneSignalPlayerId = result.OneSignalPlayerId,
                    LoginTime = result.LoginTime,
                    CreatedAt = result.CreatedAt
                };


                return (true, "Latest record updated successfully with PlayerId", resultDto);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to update record: {ex.Message}", null);
            }
        }

        public async Task<bool> SendSecurityNotificationIfMatchFoundAsync(
                SecurityNotificationRequestDto request,
                string? deviceModel = null,
                string? machineName = null,
                string? oneSignalPlayerId = null,string? link = null)
        {
            try
            {
                if (request.UserId <= 0)
                {
                    return false;
                }

                // Get device info from user agent
                var parsedDetails = ParseUserAgentForMatching(request.UserAgent);

                // Get OneSignalPlayerId from user's preferences if not provided
                if (string.IsNullOrWhiteSpace(oneSignalPlayerId))
                {
                    var preference = await _repository.GetByUserIdAsync(request.UserId);
                    oneSignalPlayerId = preference?.OneSignalPlayerId;
                }

                // Create match request
                var matchRequest = new SystemDetailsMatchRequestDto
                {
                    UserId = request.UserId,
                    IPAddress = request.IPAddress,
                    UserAgent = request.UserAgent,
                    DeviceModel = deviceModel,
                    MachineName = machineName,
                    BrowserName = parsedDetails.BrowserName,
                    BrowserVersion = parsedDetails.BrowserVersion,
                    OSVersion = parsedDetails.OSVersion,
                    OneSignalPlayerId = oneSignalPlayerId
                };

                // Check for matching record
                var matchResult = await CheckForMatchingSystemDetailsAsync(matchRequest);

                if (matchResult.IsMatch && !string.IsNullOrWhiteSpace(matchResult.OneSignalPlayerId))
                {
                    // Send OneSignal notification with custom title/message
                    var notificationSent = await SendCustomSecurityNotificationAsync(
                        matchResult.OneSignalPlayerId,
                        request,
                        matchResult.MatchingRecord,link);

                    return notificationSent;
                }

                return false;
            }
            catch (Exception ex)
            {
                // Log error if needed
                return false;
            }
        }

        private async Task<bool> SendCustomSecurityNotificationAsync(
            string playerId,
            SecurityNotificationRequestDto request,
            UserSystemDetailsDto? matchingRecord, string? link = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_oneSignalAppId) || string.IsNullOrWhiteSpace(_oneSignalApiKey))
                {
                    return false;
                }

                // Use custom title/message if provided, otherwise use defaults
                string title = !string.IsNullOrWhiteSpace(request.CustomTitle)
                    ? request.CustomTitle
                    : string.Empty;// "New Login Detected";

                string message = !string.IsNullOrWhiteSpace(request.CustomMessage)
                    ? request.CustomMessage
                    : string.Empty;// "A new login was detected from a different device or location.";

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
                {
                    return false;
                }
                
                var notification = new OneSignalNotificationDto
                {
                    Url = link,
                    AppId = _oneSignalAppId,
                    IncludePlayerIds = new[] { playerId },
                    Headings = new NotificationContent { En = title },
                    Contents = new NotificationContent { En = message },
                    ChromeWebIcon = _notificationIconUrl,
                    FirefoxIcon = _notificationIconUrl,
                    Data = new
                    {
                        userId = request.UserId,
                        eventType = "security_alert",
                        timestamp = DateTime.UtcNow,
                        currentLogin = new
                        {
                            ipAddress = request.IPAddress,
                            browser = GetBrowserFromUserAgent(request.UserAgent),
                            time = DateTime.UtcNow
                        },
                        previousLogin = matchingRecord != null ? new
                        {
                            ipAddress = matchingRecord.IPAddress,
                            browser = matchingRecord.BrowserName,
                            loginTime = matchingRecord.LoginTime
                        } : null
                    }
                };

                var jsonContent = JsonSerializer.Serialize(notification, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_oneSignalApiUrl}/notifications")
                {
                    Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
                };

                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", _oneSignalApiKey);

                var response = await _httpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private string GetBrowserFromUserAgent(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return "Unknown";

            var browserInfo = ParseBrowser(userAgent);
            return browserInfo.Name ?? "Unknown";
        }

        public async Task<(bool IsSuccess, string Message)> SendNotificationAsync(
            GenericNotificationRequestDto request)
        {
            try
            {
                if (request.UserId <= 0)
                {
                    return (false, "Valid UserId is required");
                }

                if (string.IsNullOrWhiteSpace(request.Title))
                {
                    return (false, "Notification title is required");
                }

                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return (false, "Notification message is required");
                }

                // Check if user has push notifications enabled
                var isEnabled = await _repository.IsEnabledForUserAsync(request.UserId);
                if (!isEnabled)
                {
                    return (true, "User has push notifications disabled, skipping notification");
                }

                // Get user's player IDs
                var playerIds = await _repository.GetEnabledPlayerIdsByUserIdsAsync(new List<int> { request.UserId });

                if (!playerIds.Any())
                {
                    return (false, "No active push notification subscriptions found for this user");
                }

                // Send OneSignal notification
                var result = await SendOneSignalNotificationAsync(
                    playerIds.ToArray(),
                    request.Title,
                    request.Message,
                    request.Data
                );

                return result;
            }
            catch (Exception ex)
            {
                return (false, $"Failed to send notification: {ex.Message}");
            }
        }
    }
}

