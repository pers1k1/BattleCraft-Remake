using System;
using System.IO;
using System.Text.Json;

namespace CustomLauncher.Core
{
    public static class ForgeInstall
    {
        public static bool Present(string gamePath)
        {
            string profile = Path.Combine(gamePath, "versions", GameVersions.ForgeProfileId);
            string manifest = Path.Combine(profile, GameVersions.ForgeProfileId + ".json");
            if (!File.Exists(manifest)) return false;

            try
            {
                using FileStream stream = File.OpenRead(manifest);
                using JsonDocument parsed = JsonDocument.Parse(stream);
                return parsed.RootElement.TryGetProperty("libraries", out JsonElement libraries)
                    && libraries.ValueKind == JsonValueKind.Array
                    && libraries.GetArrayLength() > 0;
            }
            catch (Exception error)
            {
                LauncherLog.Write($"[LOADER] Профиль Forge непригоден, ставим заново: {error.Message}");
                return false;
            }
        }

        public static void RemoveOtherProfiles(string gamePath)
        {
            string versions = Path.Combine(gamePath, "versions");
            if (!Directory.Exists(versions)) return;

            foreach (string profile in Directory.GetDirectories(versions))
            {
                string name = Path.GetFileName(profile);
                if (name == GameVersions.ForgeProfileId) continue;
                if (!name.Contains(GameVersions.Minecraft) || !name.ToLower().Contains("forge")) continue;

                try { Directory.Delete(profile, true); }
                catch (Exception error) { LauncherLog.Write($"[LOADER] Профиль {name} не удалён: {error.Message}"); }
            }
        }
    }
}
