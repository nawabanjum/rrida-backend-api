using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CloverBackoffice.Components.Helpers
{
    /// <summary>
    /// Works out WHICH ORGANISATION an email address belongs to.
    ///
    /// Manager onboarding no longer asks the caller which company a manager goes
    /// into — it reads it off their email address. "abc" is the organisation in
    /// every one of these:
    ///
    ///     manager@abc.com          manager@abc.co.uk
    ///     manager@abc.co.us        manager@mail.abc.com
    ///
    /// The hard part is the suffix. Naively taking the label before the last dot
    /// makes "abc.co.uk" an organisation called "co", which would pool every
    /// unrelated UK company into one tenant. So the suffix is worked out first
    /// and the organisation is the label immediately in front of it.
    ///
    /// This is a pragmatic subset of the Public Suffix List, not the real thing:
    /// the full list is ~9,000 rules that need refreshing, and pulling it in as a
    /// dependency for one lookup is not worth it. Two rules cover the cases that
    /// occur in practice:
    ///
    ///   1. An explicit table of multi-part suffixes that are NOT ccTLD-shaped
    ///      (com.br, co.jp …) or that need to be certain (co.uk).
    ///   2. A generic rule for two-letter country TLDs: a well-known
    ///      second-level label in front of one (co.us, com.au, or.jp …) is part
    ///      of the suffix.
    ///
    /// Anything unrecognised falls back to "the last label is the suffix", which
    /// is right for every generic TLD (.com, .io, .agency …).
    /// </summary>
    public static class EmailOrganizationResolver
    {
        /// <summary>
        /// What an email address says about the organisation behind it.
        /// </summary>
        public sealed class OrganizationIdentity
        {
            /// <summary>The full host from the address, lower-cased: "mail.abc.co.uk".</summary>
            public string Host { get; init; } = string.Empty;

            /// <summary>Host plus one label: "abc.co.uk". What gets stored on Profile.DomainName.</summary>
            public string RegistrableDomain { get; init; } = string.Empty;

            /// <summary>
            /// The organisation label, lower-cased: "abc". This is the identity —
            /// abc.com, abc.co.uk and abc.co.us all resolve to it, so managers on
            /// any of them land in the same company.
            /// </summary>
            public string OrganizationKey { get; init; } = string.Empty;

            /// <summary>The organisation key made presentable: "Abc". Used to name a company created from scratch.</summary>
            public string DisplayName { get; init; } = string.Empty;
        }

        // Multi-part suffixes that the generic ccTLD rule below would miss or get
        // wrong. Kept deliberately short — every entry here is one that has been
        // seen on real business email.
        private static readonly HashSet<string> MultiPartSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            // United Kingdom
            "co.uk", "org.uk", "ac.uk", "gov.uk", "net.uk", "sch.uk", "ltd.uk", "plc.uk", "me.uk", "nhs.uk",
            // Ireland / Europe
            "co.at", "or.at", "com.de", "com.es", "com.pl", "com.ua", "com.tr", "com.cy", "com.mt", "com.gr",
            // Asia-Pacific
            "co.jp", "or.jp", "ne.jp", "ac.jp", "go.jp", "com.cn", "net.cn", "org.cn", "gov.cn",
            "com.hk", "com.tw", "com.sg", "com.my", "com.ph", "com.vn", "co.th", "co.id", "co.kr", "or.kr",
            "com.au", "net.au", "org.au", "edu.au", "gov.au", "co.nz", "net.nz", "org.nz", "ac.nz", "govt.nz",
            "co.in", "net.in", "org.in", "ac.in", "edu.in", "gov.in", "firm.in", "gen.in", "ind.in",
            // Middle East / Africa
            "co.il", "com.sa", "com.qa", "com.kw", "com.bh", "com.om", "com.jo", "com.lb", "com.eg",
            "co.za", "org.za", "web.za", "com.ng", "com.gh", "co.ke", "com.ke",
            // Americas
            "com.br", "net.br", "org.br", "com.mx", "com.ar", "com.co", "com.pe", "com.ve", "com.ec",
            "com.uy", "com.bo", "com.py", "co.cr", "com.do", "com.pa", "com.gt", "com.sv", "com.hn", "com.ni",
            // South Asia
            "com.pk", "com.bd", "com.np", "com.lk"
        };

        // Second-level labels that act as a public suffix when they sit in front
        // of a two-letter country TLD. This is what makes "abc.co.us" resolve to
        // "abc" without needing every country enumerated above.
        private static readonly HashSet<string> CountrySecondLevelLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "co", "com", "net", "org", "gov", "edu", "ac", "mil", "sch", "or", "ne", "go", "govt", "asn", "id"
        };

        /// <summary>
        /// Reads the organisation out of an email address.
        /// Returns null when the address is not one that names an organisation:
        /// no "@", an empty local part or host, a host with no dot, or a host that
        /// is nothing but a public suffix ("someone@co.uk").
        /// </summary>
        public static OrganizationIdentity? FromEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            var at = email.LastIndexOf('@');
            if (at <= 0 || at == email.Length - 1)
                return null;

            // A second "@" earlier in the string is not an address this should
            // guess at, so the local part is required to be free of them.
            if (email.IndexOf('@') != at)
                return null;

            return FromHost(email[(at + 1)..]);
        }

        /// <summary>
        /// The same resolution starting from a bare host, so a stored
        /// Company.DomainName can be turned into the organisation key it belongs
        /// to without reconstructing an address around it.
        /// </summary>
        public static OrganizationIdentity? FromHost(string? host)
        {
            var normalized = (host ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
            if (normalized.Length == 0)
                return null;

            var labels = normalized.Split('.');
            if (labels.Length < 2 || labels.Any(l => l.Length == 0))
                return null;

            // Hosts only; an address whose domain part is an IP literal or
            // contains anything unexpected names no organisation.
            if (!labels.All(IsHostLabel))
                return null;

            var suffixLabelCount = SuffixLabelCount(labels);

            // "someone@co.uk" — the host IS the suffix, so there is no
            // organisation label in front of it.
            if (labels.Length <= suffixLabelCount)
                return null;

            var organizationIndex = labels.Length - suffixLabelCount - 1;
            var organizationKey = labels[organizationIndex];

            return new OrganizationIdentity
            {
                Host = normalized,
                RegistrableDomain = string.Join('.', labels.Skip(organizationIndex)),
                OrganizationKey = organizationKey,
                DisplayName = ToDisplayName(organizationKey)
            };
        }

        /// <summary>
        /// Convenience for the common case — just the organisation key, or null.
        /// </summary>
        public static string? OrganizationKeyFromHost(string? host) => FromHost(host)?.OrganizationKey;

        // How many trailing labels are the public suffix.
        private static int SuffixLabelCount(string[] labels)
        {
            if (labels.Length >= 2)
            {
                var lastTwo = $"{labels[^2]}.{labels[^1]}";
                if (MultiPartSuffixes.Contains(lastTwo))
                    return 2;

                // co.us, com.au, or.jp … — a known second-level label sitting in
                // front of a country TLD.
                if (labels[^1].Length == 2 && CountrySecondLevelLabels.Contains(labels[^2]))
                    return 2;
            }

            return 1;
        }

        // "abc" → "Abc", "acme-corp" → "Acme-Corp". Only ever used to name a
        // company that is being created from nothing; an admin can rename it.
        private static string ToDisplayName(string organizationKey)
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(organizationKey);
        }

        private static bool IsHostLabel(string label)
        {
            // Letters, digits and hyphens — the shape of a DNS label. Deliberately
            // excludes digit-only labels so "user@10.0.0.1" resolves to nothing
            // rather than to an organisation called "0".
            var hasLetter = false;

            foreach (var c in label)
            {
                if (char.IsLetter(c))
                {
                    hasLetter = true;
                    continue;
                }

                if (!char.IsDigit(c) && c != '-')
                    return false;
            }

            return hasLetter;
        }
    }
}
