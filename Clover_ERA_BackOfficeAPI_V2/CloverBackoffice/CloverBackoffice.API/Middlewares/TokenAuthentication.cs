
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;
using Microsoft.Extensions.DependencyInjection;
using CloverBackoffice.Services.Interfaces;

namespace CloverBackoffice.API.Middlewares
{
    public class TokenAuthentication
    {
        private readonly RequestDelegate _next;

        public TokenAuthentication(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var requestPath = context.Request.Path.ToString();
            // 1. Skip logic
            var skipPaths = new[]
            {
                // The following paths are intentionally kept public for application functionality.
                // They are required to allow anonymous access for login, registration, password reset, etc.
                // Suppress SonarQube "S2068" or similar security hotspot warnings as these are not credentials.
                // NOSONAR
                "/api/profile/login",

                // Self-service registration. Both are listed in full on purpose: the
                // matching below is StartsWith, so "/api/profile/Create" alone would
                // silently be what keeps CreateTrial public, and removing it later
                // would break trial sign-up with nothing in the diff to explain it.
                "/api/profile/Create",
                "/api/profile/CreateTrial",

                "/api/profile/ValidateDomain",
                "/api/profile/add-unit",
                "/api/profile/forgot-password",
                "/api/profile/reset-password",
                "/api/profile/verify-email",

                // "/api/profile/upsert-user" was here and has been REMOVED. Adding
                // a user is a Profile Admin action: it must carry a token, the
                // caller's profile is what the new user is created under, and the
                // tenant guard is deliberately silent when no user resolves from
                // the token — so leaving it public left the one endpoint that
                // places a user inside a tenant with no tenant check at all. The
                // action already refused a missing token itself; this makes the
                // middleware refuse it first, which is what the guard needs.

                "/api/profile/check-name-exist",
                "/api/profile/check-email-exist",
                "/api/profile/refresh-token",
                "/api/embedding/open-ai",
                "/api/rag/openai-query",
                "/api/FeedbackAnalysis/upload-prompt-data-file",
                "/api/rag/batches-job-status",
                "/api/document-embeddings",
                "/api/payment/webhook",
                "/api/prompt-management/workbench-run",
                "/api/clovera-personal-snapshot",
                "/api/azure-marketplace/webhook",
                "/api/azure-marketplace/landing",

                // Mobile app. Every api/v1/* route is app-facing with no user
                // login; they are gated by PersonalAppTokenAuthorize instead.
                "/api/v1/",

                // Custom questions, app-facing half. These two live on the back office
                // controller because they share its service, but the app is the caller:
                // it reads what its unit should answer today and posts the answer back.
                // Gated by PersonalAppTokenAuthorize like the api/v1/* routes above.
                //
                // Listed in full, not by prefix: the matching below is StartsWith, and
                // "/api/team-daily-question/add-custom-question" would also cover
                // add-custom-question — the authoring endpoint, which stays on the back
                // office login.
                "/api/team-daily-question/custom-questions",
                "/api/team-daily-question/add-custom-question-feedback",

                // Where an invited manager lands after setting their password. It
                // is [AllowAnonymous] on the action and always was — but this
                // middleware runs BEFORE MVC and never sees that attribute, so
                // without this line it answered 401 to the one caller it exists
                // for: somebody who has just set a password and has no token yet.
                //
                // Listed in full rather than by prefix: the matching below is
                // StartsWith, and "/api/manager-onboarding" would make
                // create-manager public too.
                "/api/manager-onboarding/post-password-landing",

                // Public pricing: the active plans and the price of N managers
                // (GET api/plans, api/plans/price). Read-only; drafts, inactive plans
                // and internal notes are never returned. The RRIDA Admin side is
                // api/admin/plans, which this prefix does not match.
                "/api/plans",

                // Liveness probe (App Service health check, deploy smoke test).
                "/health"

            };
            if (skipPaths.Any(path =>
              requestPath.StartsWith(path, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }


            // 2. Read token from header
            var token = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(token) && token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring("Bearer ".Length).Trim();
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: Token missing or invalid format.");
                return;
            }

            // 3. Resolve TokenReaderService
            var tokenReaderService = context.RequestServices.GetService<ITokenReaderService>();
            if (tokenReaderService == null)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("Internal Server Error: Token service unavailable.");
                return;
            }


            // 4. Validate token
            var userId = tokenReaderService.GetUserIdFromToken();
            if (userId == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: Invalid token.");
                return;
            }

            // 5. Optionally: set user info in HttpContext.Items
            context.Items["UserId"] = userId;

            await _next(context);
        }
    }
}
