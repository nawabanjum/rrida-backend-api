using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    public class AzureMarketplaceService : IAzureMarketplaceService
    {
        private const string TokenCacheKey = "AzureMarketplace:AccessToken";

        private readonly HttpClient _http;
        private readonly AzureMarketplaceSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AzureMarketplaceService> _logger;

        public AzureMarketplaceService(
            HttpClient http,
            IOptions<AzureMarketplaceSettings> settings,
            IMemoryCache cache,
            ILogger<AzureMarketplaceService> logger)
        {
            _http = http;
            _settings = settings.Value;
            _cache = cache;
            _logger = logger;

            if (_http.BaseAddress == null && !string.IsNullOrWhiteSpace(_settings.BaseUrl))
                _http.BaseAddress = new Uri(_settings.BaseUrl);
        }

        public async Task<AzureMarketplaceResolvedSubscription> ResolveAsync(string marketplaceToken, CancellationToken ct = default)
        {
            var token = await GetAccessTokenAsync(ct);
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"/api/saas/subscriptions/resolve?api-version={_settings.ApiVersion}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("x-ms-marketplace-token", marketplaceToken);
            req.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Marketplace Resolve failed {Status} {Body}", resp.StatusCode, body);
                throw new InvalidOperationException($"Resolve failed: {resp.StatusCode} - {body}");
            }

            var resolved = JsonSerializer.Deserialize<AzureMarketplaceResolvedSubscription>(body);
            if (resolved == null)
                throw new InvalidOperationException("Resolve returned empty body.");
            return resolved;
        }

        public async Task<bool> ActivateAsync(Guid subscriptionId, string planId, int quantity, CancellationToken ct = default)
        {
            var token = await GetAccessTokenAsync(ct);
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"/api/saas/subscriptions/{subscriptionId}/activate?api-version={_settings.ApiVersion}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new { planId, quantity };
            req.Content = JsonContent.Create(payload);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("Marketplace Activate failed {Status} {Body}", resp.StatusCode, body);
                return false;
            }
            return true;
        }

        public async Task<AzureMarketplaceSubscriptionDetails?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            var token = await GetAccessTokenAsync(ct);
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"/api/saas/subscriptions/{subscriptionId}?api-version={_settings.ApiVersion}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<AzureMarketplaceSubscriptionDetails>(body);
        }

        public async Task<AzureMarketplaceOperation?> GetOperationAsync(Guid subscriptionId, Guid operationId, CancellationToken ct = default)
        {
            var token = await GetAccessTokenAsync(ct);
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"/api/saas/subscriptions/{subscriptionId}/operations/{operationId}?api-version={_settings.ApiVersion}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("GetOperation returned {Status} for sub {SubId} op {OpId}",
                    resp.StatusCode, subscriptionId, operationId);
                return null;
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<AzureMarketplaceOperation>(body);
        }

        public async Task<bool> PatchOperationAsync(Guid subscriptionId, Guid operationId, string status, string? planId = null, int? quantity = null, CancellationToken ct = default)
        {
            var token = await GetAccessTokenAsync(ct);
            using var req = new HttpRequestMessage(HttpMethod.Patch,
                $"/api/saas/subscriptions/{subscriptionId}/operations/{operationId}?api-version={_settings.ApiVersion}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new Dictionary<string, object?> { ["status"] = status };
            if (!string.IsNullOrEmpty(planId)) payload["planId"] = planId;
            if (quantity.HasValue) payload["quantity"] = quantity.Value;
            req.Content = JsonContent.Create(payload);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("PatchOperation failed {Status} {Body}", resp.StatusCode, body);
                return false;
            }
            return true;
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken ct)
        {
            if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
                return cached!;

            var tokenUrl = $"https://login.microsoftonline.com/{_settings.TenantId}/oauth2/token";
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("grant_type", "client_credentials"),
                new KeyValuePair<string,string>("client_id", _settings.ClientId),
                new KeyValuePair<string,string>("client_secret", _settings.ClientSecret),
                new KeyValuePair<string,string>("resource", _settings.ResourceId)
            });

            using var http = new HttpClient();
            using var resp = await http.PostAsync(tokenUrl, form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Marketplace token fetch failed {Status} {Body}", resp.StatusCode, body);
                throw new InvalidOperationException($"Token request failed: {resp.StatusCode}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var accessToken = root.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("access_token missing.");

            var ttl = root.TryGetProperty("expires_in", out var exp) && int.TryParse(exp.GetString(), out var seconds)
                ? Math.Max(seconds - 60, 60)
                : 3000;

            _cache.Set(TokenCacheKey, accessToken, TimeSpan.FromSeconds(ttl));
            return accessToken;
        }
    }
}
