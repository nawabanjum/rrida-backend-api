using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Services.Interfaces;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloverBackoffice.API.Middlewares
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, IServiceProvider serviceProvider, ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // SECURITY FIX: Ensure we only log sanitized exceptions
                var sanitizedException = SanitizeException(ex);

                _logger.LogError(sanitizedException, "Unhandled exception caught in GlobalExceptionMiddleware");

                // FIX: Pass sanitized exception to LogExceptionAsync, not original
                await LogExceptionAsync(_serviceProvider, context, sanitizedException);

                if (context.Response.HasStarted)
                {
                    _logger.LogWarning("Cannot write error response because the response has already started.");
                    throw;
                }

                try
                {
                    context.Response.Clear();
                    context.Response.StatusCode = GetSafeStatusCode(ex);
                    context.Response.ContentType = "application/json";

                    var errorResponse = JsonSerializer.Serialize(new
                    {
                        Message = GetSafeUserMessage(ex),
                        ErrorId = Guid.NewGuid().ToString(),
                        Timestamp = DateTime.UtcNow
                    });

                    await context.Response.WriteAsync(errorResponse);
                }
                catch (ObjectDisposedException disposeEx)
                {
                    // Log sanitized version of this exception too
                    var sanitizedDisposeEx = SanitizeException(disposeEx);
                    _logger.LogWarning(sanitizedDisposeEx, "Cannot write error response because the response stream is already closed.");
                }
            }
        }


        private Exception SanitizeException(Exception ex)
        {
            if (ex == null) return null;

            try
            {
                // Create a sanitized version of the same exception type
                var sanitizedMessage = SanitizeMessage(ex.Message);

                // Try to create instance with sanitized message
                Exception sanitizedException;
                try
                {
                    sanitizedException = (Exception)Activator.CreateInstance(ex.GetType(), sanitizedMessage);
                }
                catch
                {
                    // Fallback to generic Exception if constructor fails
                    sanitizedException = new Exception(sanitizedMessage);
                }

                // Preserve stack trace if possible (safer approach)
                try
                {
                    var stackTraceField = typeof(Exception).GetField("_stackTraceString",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    stackTraceField?.SetValue(sanitizedException, SanitizeStackTrace(ex.StackTrace));
                }
                catch
                {
                    // Silently continue if reflection fails
                }

                // Recursively sanitize inner exceptions
                if (ex.InnerException != null)
                {
                    // Use reflection to set inner exception (it's read-only)
                    try
                    {
                        var innerExceptionField = typeof(Exception).GetField("_innerException",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        innerExceptionField?.SetValue(sanitizedException, SanitizeException(ex.InnerException));
                    }
                    catch
                    {
                        // If we can't set inner exception, that's okay
                    }
                }

                return sanitizedException;
            }
            catch
            {
                // Ultimate fallback - return a completely generic exception
                return new Exception("Exception details sanitized for security");
            }
        }

        private string SanitizeStackTrace(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return stackTrace;

            stackTrace = Regex.Replace(stackTrace, @"C:\\Users\\[^\\]+\\", @"C:\Users\***\", RegexOptions.IgnoreCase);
            stackTrace = Regex.Replace(stackTrace, @"/home/[^/]+/", @"/home/***/", RegexOptions.IgnoreCase);
            stackTrace = Regex.Replace(stackTrace, @"\\.*\\(?=src\\|bin\\)", @"\***\", RegexOptions.IgnoreCase);

            return stackTrace;
        }

        private string SanitizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            var patterns = new Dictionary<string, string>
        {
            { @"(password|pwd|pass)[=:\s]*[^\s;,)]+", "password=***" },
            { @"(token|auth|bearer)[=:\s]*[^\s;,)]+", "token=***" },
            { @"(key|secret|api[_-]?key)[=:\s]*[^\s;,)]+", "key=***" },
            { @"(connection\s*string|connstring|connstr)[=:\s][^;)]+", "connectionstring=***" },
            { @"(credit|debit|card)[\s\-_]?number[=:\s]*[\d\-]+", "cardnumber=***" },
            { @"(ssn|social\s*security)[=:\s]*[\d\-]+", "ssn=***" },
            { @"(email|e-mail|mail)[=:\s]*[^\s;,)]+@[^\s;,)]+", "email=***" },
            { @"(phone|tel|mobile)[=:\s]*[\+]?[\d\-\(\)\s]+", "phone=***" },
            { @"(ip|address)[=:\s]*\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}", "ip=***" },
            { @"(user|username|login)[=:\s]*[^\s;,)]+", "user=***" }
        };

            foreach (var pattern in patterns)
            {
                message = Regex.Replace(message, pattern.Key, pattern.Value, RegexOptions.IgnoreCase);
            }

            return message;
        }

        private int GetSafeStatusCode(Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException => 401,
                ArgumentException => 400,
                FileNotFoundException => 404,
                DirectoryNotFoundException => 404,
                TimeoutException => 408,
                InvalidOperationException => 409,
                NotSupportedException => 501,
                _ => 500
            };
        }

        private string GetSafeUserMessage(Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException => "Access denied",
                ArgumentException => "Invalid request",
                FileNotFoundException => "Resource not found",
                DirectoryNotFoundException => "Resource not found",
                TimeoutException => "Request timeout",
                InvalidOperationException => "Operation not allowed",
                NotSupportedException => "Operation not supported",
                _ => "An unexpected error occurred"
            };
        }
        private static async Task LogExceptionAsync(IServiceProvider serviceProvider, HttpContext context, Exception ex)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var auditLogsService = scope.ServiceProvider.GetRequiredService<IAuditLogsService>();

                int? userId = null;
                if (context?.User?.Identity?.IsAuthenticated == true)
                {
                    var userIdClaim = context.User.FindFirst("id")?.Value;
                    userId = int.TryParse(userIdClaim, out var uid) ? uid : (int?)null;
                }

                var log = new AuditLogs
                {
                    Timestamp = DateTime.UtcNow,
                    UserId = userId,
                    Endpoint = context?.Request?.Path.Value ?? "Unknown",
                    HttpMethod = "ERROR",
                    RequestData = "",
                    ResponseData = ex.ToString(),
                    ActionType = "EXCEPTION"
                };

                await auditLogsService.AddAuditLogsAsync(log);
            }
            catch (Exception logEx)
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionMiddleware");
                logger.LogError(logEx, "Failed to save exception log");
            }
        }
    }

}
