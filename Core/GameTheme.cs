using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CustomLauncher.Core
{
    public static class GameTheme
    {
        public const string DefaultPrimary = "#120A16";
        public const string DefaultAccent = "#F7CAD0";

        private const string ThemeFolderName = "launcher_theme";
        private const string ThemeFileName = "theme.json";

        private static readonly UTF8Encoding Utf8WithoutBom = new(false);

        public static void Publish(string gameDirectory, string? primary, string? accent)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
                return;

            try
            {
                string themeDirectory = Path.Combine(gameDirectory, ThemeFolderName);
                Directory.CreateDirectory(themeDirectory);

                var theme = new JObject
                {
                    ["primary"] = Normalize(primary, DefaultPrimary),
                    ["accent"] = Normalize(accent, DefaultAccent)
                };

                string path = Path.Combine(themeDirectory, ThemeFileName);
                string content = theme.ToString();

                if (File.Exists(path) && File.ReadAllText(path, Utf8WithoutBom) == content)
                    return;

                File.WriteAllText(path, content, Utf8WithoutBom);
            }
            catch (Exception ex)
            {
                LauncherLog.Write("[ERR] GameTheme.Publish: " + ex.Message);
            }
        }

        private static string Normalize(string? hex, string fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return fallback;

            string digits = hex.Trim().TrimStart('#').ToUpperInvariant();

            if (digits.Length == 8)
                digits = digits.Substring(2);

            if (digits.Length == 3)
                digits = string.Concat(digits[0], digits[0], digits[1], digits[1], digits[2], digits[2]);

            if (digits.Length != 6)
                return fallback;

            foreach (char symbol in digits)
                if (!Uri.IsHexDigit(symbol))
                    return fallback;

            return "#" + digits;
        }
    }
}
