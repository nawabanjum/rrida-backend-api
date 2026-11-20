using System;
using System.Linq;
using System.Threading.Tasks;
using CloverBackoffice.Components.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloverBackoffice.API.Filters
{
    /// <summary>
    /// Validates a static API token for mobile-facing endpoints that have no user
    /// login. The token is read from configuration key
    /// <c>CloveraPersonalApp:ApiToken</c>. Callers must send it as
    /// <c>X-Api-Key: &lt;token&gt;</c> or <c>Authorization: Bearer &lt;token&gt;</c>.
    /// Apply at controller or action level (e.g. <c>[PersonalAppTokenAuthorize]</c>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class PersonalAppTokenAuthorizeAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var expected = config["CloveraPersonalApp:ApiToken"];

            if (string.IsNullOrWhiteSpace(expected))
            {
                context.Result = new ObjectResult(
                    ApiResponseHandler.Fail<string>("Personal App API token is not configured."))
                { StatusCode = StatusCodes.Status500InternalServerError };
                return;
            }

            var provided = ExtractToken(context.HttpContext.Request);
            if (string.IsNullOrEmpty(provided) || !FixedTimeEquals(provided, expected))
            {
                context.Result = new ObjectResult(
                    ApiResponseHandler.Fail<string>("Unauthorized: invalid or missing API token."))
                { StatusCode = StatusCodes.Status401Unauthorized };
                return;
            }

            await next();
        }

        private static string? ExtractToken(HttpRequest req)
        {
            var apiKey = req.Headers["X-Api-Key"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey.Trim();

            var auth = req.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return auth.Substring("Bearer ".Length).Trim();

            // The Clover ERA mobile app was built against Azure Functions, which takes
            // its key as x-functions-key or ?code=. Those two carriers are accepted
            // here so the app can be repointed at this API by changing the host name
            // alone. The token itself is still the single configured
            // CloveraPersonalApp:ApiToken — this adds no second credential, only two
            // more places to find the same one.
            var functionsKey = req.Headers["x-functions-key"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(functionsKey)) return functionsKey.Trim();

            var codeQuery = req.Query["code"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(codeQuery)) return codeQuery.Trim();

            return null;
        }

        // Length-constant comparison to avoid leaking the token via timing.
        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
