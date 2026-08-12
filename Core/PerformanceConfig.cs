using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CustomLauncher.Core
{
    public static class PerformanceConfig
    {
        public static void Apply(string gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
                return;

            string configDirectory = Path.Combine(gameDirectory, "config");
            Directory.CreateDirectory(configDirectory);

            DisableForgeVersionCheck(configDirectory);

            if (HasMod(gameDirectory, "spark"))
                DisableSparkBackgroundProfiler(configDirectory);
        }

        private static bool HasMod(string gameDirectory, string modFilePrefix)
        {
            string modsDirectory = Path.Combine(gameDirectory, "mods");

            return Directory.Exists(modsDirectory)
                && Directory.EnumerateFiles(modsDirectory, $"{modFilePrefix}*.jar").Any();
        }

        private static void DisableForgeVersionCheck(string configDirectory)
        {
            string path = Path.Combine(configDirectory, "fml.toml");

            if (!File.Exists(path))
            {
                File.WriteAllText(path, "versionCheck = false\n");
                return;
            }

            string[] lines = File.ReadAllLines(path);
            bool replaced = false;

            for (int index = 0; index < lines.Length; index++)
            {
                if (!lines[index].TrimStart().StartsWith("versionCheck"))
                    continue;

                lines[index] = "versionCheck = false";
                replaced = true;
            }

            File.WriteAllLines(path, replaced ? lines : lines.Append("versionCheck = false"));
        }

        private static void DisableSparkBackgroundProfiler(string configDirectory)
        {
            string sparkDirectory = Path.Combine(configDirectory, "spark");
            string path = Path.Combine(sparkDirectory, "config.json");

            Directory.CreateDirectory(sparkDirectory);

            JObject settings = File.Exists(path)
                ? JObject.Parse(File.ReadAllText(path))
                : new JObject();

            settings["backgroundProfiler"] = false;
            File.WriteAllText(path, settings.ToString());
        }
    }
}
