using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CloverBackoffice.API.Filters
{
    /// <summary>
    /// Drops named endpoints from the generated Swagger document.
    ///
    /// Written for a narrow, temporary job: when a developer is integrating one
    /// flow, the endpoints that look like the right one but are not are the
    /// expensive mistake. Hiding those few is far less disruptive than
    /// allow-listing the twenty this API happens to need, which would have to be
    /// revisited every time the flow grows.
    ///
    /// Driven entirely by configuration — <c>Swagger:HiddenEndpoints</c> — so
    /// putting an endpoint back is an appsettings change, not a code change and a
    /// redeploy. An absent or empty section hides nothing, which is what every
    /// environment that does not set it gets.
    ///
    /// Two entry shapes:
    ///   "POST /api/company/upsert-user"   hides that one verb
    ///   "/api/company/upsert-user"        hides every verb on the path
    ///
    /// The path is the ROUTE TEMPLATE as Swagger emits it, so parameters keep
    /// their braces: "/api/company/user/{id}".
    ///
    /// This is presentation only. A hidden endpoint is still routed, still
    /// authorised the same way, and still callable by anyone who knows the URL —
    /// never use it as an access control.
    /// </summary>
    public sealed class HiddenEndpointsDocumentFilter : IDocumentFilter
    {
        public const string ConfigurationSection = "Swagger:HiddenEndpoints";

        private readonly HashSet<string> _hiddenPaths;
        private readonly HashSet<(string Path, OperationType Method)> _hiddenOperations;

        public HiddenEndpointsDocumentFilter(IConfiguration configuration)
        {
            _hiddenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _hiddenOperations = new HashSet<(string, OperationType)>();

            var entries = configuration
                .GetSection(ConfigurationSection)
                .Get<string[]>() ?? Array.Empty<string>();

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                var parts = entry.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // "VERB /path" when the first token is a real HTTP method,
                // otherwise the whole entry is a path and every verb on it goes.
                if (parts.Length >= 2 && Enum.TryParse<OperationType>(parts[0], ignoreCase: true, out var method))
                {
                    _hiddenOperations.Add((Normalise(string.Join(' ', parts.Skip(1))), method));
                }
                else
                {
                    _hiddenPaths.Add(Normalise(entry));
                }
            }
        }

        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            if (swaggerDoc?.Paths == null || (_hiddenPaths.Count == 0 && _hiddenOperations.Count == 0))
                return;

            // Snapshot the keys: the loop removes from the collection it walks.
            foreach (var path in swaggerDoc.Paths.Keys.ToList())
            {
                var normalised = Normalise(path);

                if (_hiddenPaths.Contains(normalised))
                {
                    swaggerDoc.Paths.Remove(path);
                    continue;
                }

                var item = swaggerDoc.Paths[path];
                if (item?.Operations == null)
                    continue;

                foreach (var method in item.Operations.Keys.ToList())
                {
                    if (_hiddenOperations.Contains((normalised, method)))
                        item.Operations.Remove(method);
                }

                // A path whose every verb was hidden would otherwise render as an
                // empty, unopenable row in the UI.
                if (item.Operations.Count == 0)
                    swaggerDoc.Paths.Remove(path);
            }
        }

        // Tolerates a missing leading slash and trailing slashes, so a
        // hand-written appsettings entry does not silently fail to match.
        private static string Normalise(string path)
        {
            var trimmed = (path ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return "/";

            if (!trimmed.StartsWith('/'))
                trimmed = "/" + trimmed;

            return trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
        }
    }
}
