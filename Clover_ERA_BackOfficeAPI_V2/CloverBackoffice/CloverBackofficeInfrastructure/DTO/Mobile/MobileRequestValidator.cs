using System;
using System.Collections.Generic;
using System.Linq;

namespace CloverBackoffice.Infrastructure.DTO.Mobile
{
    /// <summary>
    /// Field validation for the mobile endpoints, producing the same failures in the
    /// same order as the FluentValidation rules the old API used.
    ///
    /// FluentValidation is not referenced by this solution and is not worth adding for
    /// seven request types, but its *semantics* matter, because the app renders the
    /// error list it gets back. Two behaviours are reproduced deliberately:
    ///
    ///   * Rules do NOT stop at the first failure. FluentValidation's default cascade
    ///     mode is Continue, so an empty email address yields both "has not been
    ///     provided" and "is invalid" — two entries, in declaration order.
    ///   * Each rule keeps its own null/empty tolerance. NotEmpty rejects whitespace;
    ///     MaximumLength, ExactLength and Matches all pass a null value, leaving the
    ///     "missing" case to NotEmpty alone rather than reporting one field twice for
    ///     the same reason.
    ///
    /// Field names are the PascalCase property names, because that is what
    /// FluentValidation put in PropertyName and what the app has been receiving.
    /// </summary>
    public sealed class MobileRequestValidator
    {
        private readonly List<MobileErrorFieldItem> _failures = new();

        public bool IsValid => _failures.Count == 0;

        public IReadOnlyList<MobileErrorFieldItem> Failures => _failures;

        /// <summary>Starts a rule chain for one field.</summary>
        public FieldRules For(string fieldName, string? value) => new(this, fieldName, value);

        /// <summary>Records a failure directly, for rules with no fluent equivalent.</summary>
        public MobileRequestValidator Add(string fieldName, string message)
        {
            _failures.Add(new MobileErrorFieldItem { FieldName = fieldName, Message = message });
            return this;
        }

        /// <summary>Builds the 400 body: the standard message plus every failure.</summary>
        public MobileErrorResponse ToErrorResponse()
        {
            var response = new MobileErrorResponse(MobileApiMessages.Request_Invalid);

            foreach (var failure in _failures)
            {
                response.Errors.Add(failure);
            }

            return response;
        }

        /// <summary>
        /// A chain of checks against one field. Every method evaluates and returns
        /// this, so all of them run — the Continue cascade described above.
        /// </summary>
        public sealed class FieldRules
        {
            private readonly string _fieldName;
            private readonly MobileRequestValidator _owner;
            private readonly string? _value;

            internal FieldRules(MobileRequestValidator owner, string fieldName, string? value)
            {
                _owner = owner;
                _fieldName = fieldName;
                _value = value;
            }

            /// <summary>Rejects null, empty and whitespace-only, as NotEmpty does.</summary>
            public FieldRules NotEmpty(string message)
            {
                if (string.IsNullOrWhiteSpace(_value))
                {
                    _owner.Add(_fieldName, message);
                }

                return this;
            }

            /// <summary>Null passes; anything longer than the limit fails.</summary>
            public FieldRules MaximumLength(int maximumLength, string message)
            {
                if (_value != null && _value.Length > maximumLength)
                {
                    _owner.Add(_fieldName, message);
                }

                return this;
            }

            /// <summary>Null passes; any other length but this one fails.</summary>
            public FieldRules ExactLength(int length, string message)
            {
                if (_value != null && _value.Length != length)
                {
                    _owner.Add(_fieldName, message);
                }

                return this;
            }

            /// <summary>Null passes; a non-match fails.</summary>
            public FieldRules Matches(System.Text.RegularExpressions.Regex pattern, string message)
            {
                if (_value != null && !pattern.IsMatch(_value))
                {
                    _owner.Add(_fieldName, message);
                }

                return this;
            }

            /// <summary>
            /// Null passes; an empty string does not. Matches the shape check
            /// FluentValidation's EmailAddress() applies — one '@', with something
            /// either side of it and no whitespace.
            /// </summary>
            public FieldRules EmailAddress(string message)
            {
                if (_value != null && !IsWellFormedEmail(_value))
                {
                    _owner.Add(_fieldName, message);
                }

                return this;
            }

            /// <summary>Rejects known free and disposable domains. Empty passes.</summary>
            public FieldRules NotFreeEmailAddress(string message)
            {
                if (MobileFreeEmailDomainSet.IsFreeEmailAddress(_value))
                {
                    _owner.Add(_fieldName, message);
                }

                return this;
            }

            private static bool IsWellFormedEmail(string value)
            {
                if (value.Length == 0) return false;
                if (value.Any(char.IsWhiteSpace)) return false;

                var atIndex = value.IndexOf('@');

                // Must have exactly one '@', with a local part and a domain part.
                return atIndex > 0
                       && atIndex == value.LastIndexOf('@')
                       && atIndex < value.Length - 1;
            }
        }
    }
}
