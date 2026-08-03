using System;
using System.Globalization;

namespace CustomLauncher.Core
{
    public static class ReleaseVersion
    {
        private const string CanonicalDateFormat = "yyyy.MM.dd";
        private const string DisplayDateFormat = "dd.MM.yy";
        private const int CanonicalDateLength = 10;
        private const int BaseRevision = 1;
        private const int HotfixRevision = 2;

        public static bool IsValid(string? raw) => TryParseStamp(raw, out _) || Version.TryParse(raw, out _);

        public static bool IsNewer(string? candidate, string? current)
        {
            bool candidateIsStamp = TryParseStamp(candidate, out var candidateStamp);
            bool currentIsStamp = TryParseStamp(current, out var currentStamp);

            if (candidateIsStamp && currentIsStamp) return candidateStamp.CompareTo(currentStamp) > 0;
            if (candidateIsStamp) return true;
            if (currentIsStamp) return false;

            return Version.TryParse(candidate, out var candidateLegacy)
                && Version.TryParse(current, out var currentLegacy)
                && candidateLegacy > currentLegacy;
        }

        public static string Display(string? raw) => TryParseStamp(raw, out var stamp)
            ? stamp.Date.ToString(DisplayDateFormat, CultureInfo.InvariantCulture) + stamp.Suffix
            : (raw ?? "").Trim();

        private readonly struct Stamp
        {
            public Stamp(DateTime date, int revision, string suffix)
            {
                Date = date;
                Revision = revision;
                Suffix = suffix;
            }

            public DateTime Date { get; }
            public int Revision { get; }
            public string Suffix { get; }

            public int CompareTo(Stamp other)
            {
                int byDate = Date.CompareTo(other.Date);
                return byDate != 0 ? byDate : Revision.CompareTo(other.Revision);
            }
        }

        private static bool TryParseStamp(string? raw, out Stamp stamp)
        {
            stamp = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string trimmed = raw.Trim();
            if (trimmed.Length < CanonicalDateLength) return false;

            if (!DateTime.TryParseExact(trimmed.Substring(0, CanonicalDateLength), CanonicalDateFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;

            string suffix = trimmed.Substring(CanonicalDateLength);
            if (!TryParseRevision(suffix, out int revision)) return false;

            stamp = new Stamp(date, revision, suffix);
            return true;
        }

        private static bool TryParseRevision(string suffix, out int revision)
        {
            revision = BaseRevision;
            if (suffix.Length == 0) return true;

            if (suffix.Equals("hotfix", StringComparison.OrdinalIgnoreCase))
            {
                revision = HotfixRevision;
                return true;
            }

            if ((suffix[0] == 'v' || suffix[0] == 'V')
                && int.TryParse(suffix.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                && parsed > BaseRevision)
            {
                revision = parsed;
                return true;
            }

            return false;
        }
    }
}
