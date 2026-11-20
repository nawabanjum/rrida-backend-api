using System.Linq;
using System.Text;

namespace CloverBackoffice.Components.Helpers
{
    /// <summary>
    /// The password rules the RRIDA screens show (ProfileRegistration/schema.ts,
    /// used by sign-up, accept-invite and reset-password): at least 10 characters,
    /// an upper and a lower case letter, and a number or a symbol.
    ///
    /// Used where an admin types a password for someone else
    /// (api/profile/activate-user), so the server enforces what the screens do.
    /// </summary>
    public static class PasswordPolicy
    {
        public const int MinLength = 10;

        /// <summary>
        /// BCrypt only reads the first 72 bytes; anything longer would be cut
        /// silently, so the user could log in with only part of what was set.
        /// </summary>
        public const int MaxBytes = 72;

        /// <summary>The first rule the password breaks, or null when it is acceptable.</summary>
        public static string? Validate(string? password)
        {
            if (string.IsNullOrEmpty(password))
                return ResponseMessages.Errors.PasswordRequired;

            if (password.Length < MinLength)
                return ResponseMessages.Errors.PasswordTooShort;

            if (Encoding.UTF8.GetByteCount(password) > MaxBytes)
                return ResponseMessages.Errors.PasswordTooLong;

            if (password.Any(char.IsWhiteSpace))
                return ResponseMessages.Errors.PasswordHasSpaces;

            if (!password.Any(char.IsLower) || !password.Any(char.IsUpper))
                return ResponseMessages.Errors.PasswordNeedsUpperAndLower;

            // schema.ts: /[\d\W]/ — a digit or anything that is not a letter, digit or underscore.
            if (!password.Any(c => char.IsDigit(c) || !(char.IsLetterOrDigit(c) || c == '_')))
                return ResponseMessages.Errors.PasswordNeedsNumberOrSymbol;

            return null;
        }
    }
}
