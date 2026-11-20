using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CloverBackoffice.API.Filters
{
    /// <summary>
    /// Keeps ONLY the named controllers in the generated Swagger document and drops
    /// every other operation.
    ///
    /// The opposite job to <see cref="HiddenEndpointsDocumentFilter"/>, and the
    /// right shape once the set of endpoints actually in use is smaller than the set
    /// that is not: this API has ~46 controllers and a handful are live, so listing
    /// the survivors is both shorter and self-maintaining — a controller added
    /// tomorrow stays hidden until somebody says otherwise.
    ///
    /// Driven entirely by configuration — <c>Swagger:VisibleControllers</c> — so
    /// putting a controller back is an appsettings change, not a code change and a
    /// redeploy. An absent or empty section shows EVERYTHING, which is what any
    /// environment that does not configure it gets.
    ///
    /// Entries are controller names WITHOUT the "Controller" suffix, matched
    /// case-insensitively:
    ///   "AccountRegistration"        one controller
    ///   "Mobile*"                    every controller whose name starts with Mobile
    /// A single trailing "*" is the only wildcard.
    ///
    /// This is presentation only. A hidden endpoint is still routed, still
    /// authorised the same way, and still callable by anyone who knows the URL —
    /// never use it as an access control.
    /// </summary>
    public sealed class VisibleControllersDocumentFilter : IDocumentFilter
    {
        public const string ConfigurationSection = "Swagger:VisibleControllers";

        private readonly HashSet<string> _exact;
        private readonly List<string> _prefixes;

        public VisibleControllersDocumentFilter(IConfiguration configuration)
        {
            _exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _prefixes = new List<string>();

            var entries = configuration
                .GetSection(ConfigurationSection)
                .Get<string[]>() ?? Array.Empty<string>();

            foreach (var entry in entries)
            {
                var name = (entry ?? string.Empty).Trim();
                if (name.Length == 0)
                    continue;

                // Tolerate "FooController" as well as "Foo" — the suffix is the
                // obvious thing to type and silently matching nothing is the worst
                // possible outcome for a filter that hides things.
                if (name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - "Controller".Length);

                if (name.EndsWith('*'))
                    _prefixes.Add(name.TrimEnd('*'));
                else
                    _exact.Add(name);
            }
        }

        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            if (swaggerDoc?.Paths == null || (_exact.Count == 0 && _prefixes.Count == 0))
                return;

            // Every (path, verb) that belongs to a controller we are keeping.
            var keep = new HashSet<(string Path, OperationType Method)>();

            foreach (var api in context.ApiDescriptions)
            {
                if (api.ActionDescriptor is not ControllerActionDescriptor action)
                    continue;

                if (!IsVisible(action.ControllerName))
                    continue;

                if (!Enum.TryParse<OperationType>(api.HttpMethod ?? string.Empty, ignoreCase: true, out var method))
                    continue;

                keep.Add((Normalise(api.RelativePath), method));
            }

            // Snapshot the keys: the loop removes from the collection it walks.
            foreach (var path in swaggerDoc.Paths.Keys.ToList())
            {
                var item = swaggerDoc.Paths[path];
                if (item?.Operations == null)
                {
                    swaggerDoc.Paths.Remove(path);
                    continue;
                }

                var normalised = Normalise(path);

                foreach (var method in item.Operations.Keys.ToList())
                {
                    if (!keep.Contains((normalised, method)))
                        item.Operations.Remove(method);
                }

                // A path whose every verb was dropped would otherwise render as an
                // empty, unopenable row in the UI.
                if (item.Operations.Count == 0)
                    swaggerDoc.Paths.Remove(path);
            }
        }

        private bool IsVisible(string controllerName)
        {
            if (_exact.Contains(controllerName))
                return true;

            foreach (var prefix in _prefixes)
            {
                if (controllerName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // Tolerates a missing leading slash and trailing slashes so an
        // ApiDescription.RelativePath ("api/profile/Create") and a Swagger document
        // path ("/api/profile/Create") compare equal.
        private static string Normalise(string path)
        {
            var trimmed = (path ?? string.Empty).Trim();

            // RelativePath can carry a query-string template; the document path never does.
            var query = trimmed.IndexOf('?');
            if (query >= 0)
                trimmed = trimmed.Substring(0, query);

            if (trimmed.Length == 0)
                return "/";

            if (!trimmed.StartsWith('/'))
                trimmed = "/" + trimmed;

            return trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
        }
    }
}
