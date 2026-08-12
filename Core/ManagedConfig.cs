using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CustomLauncher.Core
{
    public static class ManagedConfig
    {
        public const string ManifestUrl =
            "https://raw.githubusercontent.com/pers1k1/vrsns/main/managed_config.json";

        public static async Task<int> ApplyAsync(string gamePath, HttpClient client)
        {
            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
                return 0;

            JObject? manifest = await DownloadManifest(client);
            if (manifest?["files"] is not JObject files)
                return 0;

            int changed = 0;

            foreach (var entry in files)
            {
                if (entry.Value is not JObject keys)
                    continue;

                string path = Path.Combine(gamePath, entry.Key.Replace('/', Path.DirectorySeparatorChar));
                changed += ApplyToFile(path, keys);
            }

            return changed;
        }

        private static async Task<JObject?> DownloadManifest(HttpClient client)
        {
            try
            {
                string url = ManifestUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return JObject.Parse(await client.GetStringAsync(url));
            }
            catch
            {
                return null;
            }
        }

        private static int ApplyToFile(string path, JObject keys)
        {
            if (!File.Exists(path))
                return 0;

            string[] lines = File.ReadAllLines(path);
            int changed = 0;

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                int separator = line.IndexOf('=');
                if (separator <= 0 || line.TrimStart().StartsWith('#'))
                    continue;

                string key = line[..separator].Trim();
                if (keys[key]?.ToString() is not string value)
                    continue;

                string replacement = BuildLine(line, separator, key, value);
                if (replacement == line)
                    continue;

                lines[index] = replacement;
                changed++;
            }

            if (changed > 0)
                File.WriteAllLines(path, lines);

            return changed;
        }

        private static string BuildLine(string original, int separator, string key, string value)
        {
            string indent = original[..(original.Length - original.TrimStart().Length)];
            bool spaced = separator > 0 && original[separator - 1] == ' ';

            return spaced ? $"{indent}{key} = {value}" : $"{indent}{key}={value}";
        }
    }
}
