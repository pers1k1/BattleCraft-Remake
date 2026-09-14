using System;
using System.Globalization;

namespace CustomLauncher.Core
{
    public static class ReleaseVersion
    {
        private const string CanonicalDateFormat = "yyyy.MM.dd";
        private const string DisplayDateFormat = "dd.MM.yy";
        private const int CanonicalDateLength = 10;
        private const int BaseStage = 1;
        private const int HotfixStage = 2;
        private const int FirstStep = 1;
        private const string HotfixSuffix = "hotfix";

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

        private readonly struct Revision
        {
            public Revision(int stage, int step)
            {
                Stage = stage;
                Step = step;
            }

            public int Stage { get; }
            public int Step { get; }

            public int CompareTo(Revision other)
            {
                int byStage = Stage.CompareTo(other.Stage);
                return byStage != 0 ? byStage : Step.CompareTo(other.Step);
            }
        }

        private readonly struct Stamp
        {
            public Stamp(DateTime date, Revision revision, string suffix)
            {
                Date = date;
                Revision = revision;
                Suffix = suffix;
            }

            public DateTime Date { get; }
            public Revision Revision { get; }
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
            if (!TryParseRevision(suffix, out var revision)) return false;

            stamp = new Stamp(date, revision, suffix);
            return true;
        }

        private static bool TryParseRevision(string suffix, out Revision revision)
        {
            revision = new Revision(BaseStage, FirstStep);
            if (suffix.Length == 0) return true;

            bool hotfix = suffix.StartsWith(HotfixSuffix, StringComparison.OrdinalIgnoreCase);
            string tail = hotfix ? suffix.Substring(HotfixSuffix.Length) : suffix;

            if (tail.Length == 0)
            {
                revision = new Revision(HotfixStage, FirstStep);
                return true;
            }

            if (!TryParseStep(tail, out int step)) return false;

            revision = hotfix ? new Revision(HotfixStage, step) : new Revision(step, FirstStep);
            return true;
        }

        private static bool TryParseStep(string tail, out int step)
        {
            step = FirstStep;
            if (tail[0] != 'v' && tail[0] != 'V') return false;
            if (!int.TryParse(tail.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)) return false;
            if (parsed <= FirstStep) return false;

            step = parsed;
            return true;
        }
    }
}
