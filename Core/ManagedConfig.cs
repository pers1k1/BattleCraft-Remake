using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
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
            JObject? templates = manifest["templates"] as JObject;
            foreach (KeyValuePair<string, JToken?> entry in files)
            {
                if (entry.Value is not JObject keys) continue;
                changed += ApplyEntry(gamePath, entry.Key, keys, templates);
            }
            return changed;
        }

        private static int ApplyEntry(string gamePath, string relative, JObject keys, JObject? templates)
        {
            try
            {
                string path = ResolvePath(gamePath, relative);
                string? template = ManagedConfigTemplates.Find(relative, keys, templates);
                return ApplyToFile(path, keys, template);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                LauncherLog.Write($"[WARN] Не удалось применить настройки {relative}: {error.Message}");
                return 0;
            }
        }

        private static string ResolvePath(string gamePath, string relative)
        {
            string normalized = relative.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized) || normalized.Contains(':'))
                throw new ArgumentException("Путь конфига должен быть относительным");

            string root = Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, normalized));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Путь конфига выходит за папку игры");
            return path;
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

        private static int ApplyToFile(string path, JObject keys, string? template)
        {
            bool exists = File.Exists(path);
            if (!exists && template == null)
            {
                LauncherLog.Write($"[WARN] Нет шаблона для создания конфига {path}");
                return 0;
            }

            string[] lines = exists ? File.ReadAllLines(path) : template!.Replace("\r", "").Split('\n');
            int changed = PatchLines(lines, keys);
            if (!exists || changed > 0) WriteFile(path, lines);
            return exists ? changed : Math.Max(1, keys.Count);
        }

        private static int PatchLines(string[] lines, JObject keys)
        {
            int changed = 0;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                int separator = line.IndexOf('=');
                if (separator <= 0 || line.TrimStart().StartsWith('#')) continue;

                string key = line[..separator].Trim();
                if (keys[key]?.ToString() is not string value) continue;

                string replacement = BuildLine(line, separator, key, value);
                if (replacement == line) continue;

                lines[index] = replacement;
                changed++;
            }
            return changed;
        }

        private static void WriteFile(string path, string[] lines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string BuildLine(string original, int separator, string key, string value)
        {
            string indent = original[..(original.Length - original.TrimStart().Length)];
            bool spaced = separator > 0 && original[separator - 1] == ' ';
            return spaced ? $"{indent}{key} = {value}" : $"{indent}{key}={value}";
        }
    }
}
