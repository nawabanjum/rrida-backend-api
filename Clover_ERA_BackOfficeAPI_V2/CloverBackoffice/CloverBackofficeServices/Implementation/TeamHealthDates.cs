using System.Globalization;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>Date input shared by the Team Health endpoints.</summary>
    public static class TeamHealthDates
    {
        private static readonly string[] Formats =
        {
            "yyyy-MM-dd", "dd-MM-yyyy", "yyyy/MM/dd", "dd/MM/yyyy"
        };

        /// <summary>
        /// "YYYY-MM-DD" (what a date input sends) or "DD-MM-YYYY", '/' also accepted.
        /// Anything else, or an impossible date such as 31-02-2026, is null.
        /// </summary>
        public static DateOnly? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateOnly.TryParseExact(value.Trim(), Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)
                ? date
                : null;
        }
    }
}
