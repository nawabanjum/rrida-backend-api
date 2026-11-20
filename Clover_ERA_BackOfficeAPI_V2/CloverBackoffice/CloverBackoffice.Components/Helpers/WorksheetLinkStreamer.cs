using System;
using System.Text;
using System.Text.RegularExpressions;

namespace CloverBackoffice.Components.Helpers
{
    /// <summary>
    /// Applies <see cref="WorksheetMatcher.LinkifyWorksheetReferences"/> to a reply that arrives
    /// in chunks. A reference is routinely split across chunk boundaries ("Worksheet " lands in
    /// one token, "E3" in the next), so a per-chunk regex would miss most of them. This holds back
    /// the trailing text that could still grow into a reference, emits everything before it, and
    /// releases the remainder on <see cref="Flush"/>.
    ///
    /// Not thread-safe — drive it from a single stream loop.
    /// </summary>
    public sealed class WorksheetLinkStreamer
    {
        // A tail that is still an unfinished reference: the word being spelled out, or the word
        // plus a code that could still gain another letter/digit ("Worksheet E" -> "Worksheet E3"
        // -> "Worksheet E30"). Anchored at both ends so it only ever matches a whole tail.
        private static readonly Regex PartialReferenceRegex = new(
            @"^Worksheets?[\s\-]{0,3}[A-Za-z]{0,2}[0-9]{0,3}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string ReferenceWord = "worksheets";

        private readonly string? _worksheetFolderPath;
        private readonly StringBuilder _pending = new();

        public WorksheetLinkStreamer(string? worksheetFolderPath = null)
        {
            _worksheetFolderPath = worksheetFolderPath;
        }

        /// <summary>
        /// Feeds one streamed chunk in and returns the text that is safe to send now — linkified,
        /// and never cutting a reference in half. Returns an empty string when the whole chunk had
        /// to be held back.
        /// </summary>
        public string Push(string? chunk)
        {
            if (!string.IsNullOrEmpty(chunk)) _pending.Append(chunk);
            if (_pending.Length == 0) return string.Empty;

            var buffered = _pending.ToString();
            var cut = SafeEmitLength(buffered);
            if (cut <= 0) return string.Empty;

            _pending.Remove(0, cut);
            return WorksheetMatcher.LinkifyWorksheetReferences(buffered.Substring(0, cut), _worksheetFolderPath)
                   ?? string.Empty;
        }

        /// <summary>
        /// Releases whatever is still held back, linkified. Call once the stream has ended —
        /// including on cancellation — or the tail is silently dropped.
        /// </summary>
        public string Flush()
        {
            if (_pending.Length == 0) return string.Empty;

            var remainder = _pending.ToString();
            _pending.Clear();
            return WorksheetMatcher.LinkifyWorksheetReferences(remainder, _worksheetFolderPath) ?? string.Empty;
        }

        /// <summary>True while text is being withheld pending more input.</summary>
        public bool HasPending => _pending.Length > 0;

        // How much of the buffer can be released: everything up to the first position from which
        // the rest is still a growing reference. Only the tail can be partial — anything earlier is
        // already terminated by the text that follows it — so the scan is bounded to the last
        // MaxReferenceLength characters.
        private static int SafeEmitLength(string buffered)
        {
            var start = Math.Max(0, buffered.Length - WorksheetMatcher.MaxReferenceLength);

            for (var i = start; i < buffered.Length; i++)
            {
                if (IsGrowingReference(buffered, i))
                    return i;
            }

            return buffered.Length;
        }

        // The tail at index i is either the reference word half-typed ("Works"), or a complete-
        // looking reference whose code could still take another character ("Worksheet E3").
        private static bool IsGrowingReference(string buffered, int index)
        {
            var length = buffered.Length - index;

            if (length <= ReferenceWord.Length &&
                string.Compare(buffered, index, ReferenceWord, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
                return true;

            return PartialReferenceRegex.IsMatch(buffered.Substring(index));
        }
    }
}
