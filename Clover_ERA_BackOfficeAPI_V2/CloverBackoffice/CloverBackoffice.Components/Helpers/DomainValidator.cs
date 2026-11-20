using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CloverBackoffice.Components.Helpers
{
    /// <summary>
    /// Matches an email/domain against the disposable-domain BLACKLIST held in
    /// CompanyDomains.json. Both lookup methods return <c>true</c> when the domain
    /// IS on the list — callers treat that as "reject this email".
    ///
    /// The blacklist is environment-gated. When <see cref="IsEnabled"/> is false
    /// every lookup returns false ("not blacklisted"), so blocked domains such as
    /// yopmail.com stay usable for testing. The JSON file is not even read in that
    /// case. See AccountRegistrationService for how the flag is resolved.
    /// </summary>
    public class DomainValidator
    {
        private readonly HashSet<string> _allowedDomains;

        /// <summary>
        /// False = blacklist switched off for this environment; every domain passes.
        /// </summary>
        public bool IsEnabled { get; }

        public DomainValidator(string jsonFilePath, bool enabled = true)
        {
            IsEnabled = enabled;

            if (!enabled)
            {
                // Skip the file read entirely — nothing will ever be looked up, and
                // this also removes a startup failure mode when the JSON is missing
                // from a non-production deployment.
                _allowedDomains = new HashSet<string>();
                return;
            }

            var json = File.ReadAllText(jsonFilePath);
            var domains = JsonSerializer.Deserialize<List<string>>(json);
            _allowedDomains = new HashSet<string>((domains ?? new List<string>()).Select(d => d.ToLower()));
        }

        /// <summary>
        /// Validates email domain (checks if domain part of email exists in JSON)
        /// </summary>
        public bool IsEmailDomainAllowed(string email)
        {
            if (!IsEnabled)
                return false;

            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
                return false;

            var domain = email.Split('@').Last().ToLower();
            return _allowedDomains.Contains(domain);
        }
        public bool IsDomainAllowed(string domain)
        {
            if (!IsEnabled)
                return false;

            return _allowedDomains.Contains(domain.ToLower());
        }
    }

}
