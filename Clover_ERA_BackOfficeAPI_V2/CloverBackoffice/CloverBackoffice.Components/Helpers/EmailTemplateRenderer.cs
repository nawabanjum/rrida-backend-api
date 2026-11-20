using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CloverBackoffice.Components.Helpers
{
    public static class EmailTemplateRenderer
    {
        public static string Render(string templateName, Dictionary<string, object> dynamicData)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"CloverBackoffice.Components.Templates.{templateName}.html";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new FileNotFoundException($"Email template '{templateName}' not found as embedded resource.");

            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd();

            foreach (var kvp in dynamicData)
            {
                html = html.Replace($"{{{{{kvp.Key}}}}}", kvp.Value?.ToString() ?? string.Empty);
            }

            return html;
        }
    }
}
