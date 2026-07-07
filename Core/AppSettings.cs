using System;
using System.Collections.Generic;
using System.IO;
using CustomLauncher.Core;
using Newtonsoft.Json;

namespace CustomLauncher
{
    public class AppSettings
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CustomLauncher");

        private static readonly string ConfigFile = Path.Combine(ConfigDir, "launcher_config.json");
        private static readonly object _fileLock = new object();

        public string Language { get; set; } = "";
        public string? PrimaryColor { get; set; } = "#14101A";
        public string? AccentColor { get; set; } = "#F7CAD0";
        public bool? BloomEnabled { get; set; } = true;
        public double? BloomStrength { get; set; } = 60.0;
        public double? ConsoleOpacity { get; set; } = 1.0;
        public string Username { get; set; } = "";
        public string UserType { get; set; } = "";
        public int RamMb { get; set; } = 4096;
        public string GamePath { get; set; } = "";
        public bool IsModpackInstalled { get; set; } = false;
        public bool DebugConsole { get; set; } = false;
        public string ModpackVersion { get; set; } = "0.0";
        public string BattleCraftModVersion { get; set; } = "0.0";
        public string ServerModpackVersion { get; set; } = "0.0";
        public string ServerBattleCraftModVersion { get; set; } = "0.0";
        public string ServerMapVersion { get; set; } = "0.0";
        public List<ServerConfig> Servers { get; set; } = new();
        public string LastActiveServerName { get; set; } = "";

        public bool IsFirstRun => string.IsNullOrWhiteSpace(Username);
        public bool HasGamePath => !string.IsNullOrWhiteSpace(GamePath);

        public static string GetConfigDir() => ConfigDir;

        public static void Save(AppSettings settings)
        {
            lock (_fileLock)
            {
                try
                {
                    if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
                    File.WriteAllText(ConfigFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
                }
                catch { }
            }
        }

        private static void MigrateServerVersions(AppSettings s)
        {
            foreach (var server in s.Servers)
            {
                if (!server.IsInstalled) continue;
                if (server.ModpackVersion == "0.0" && s.ServerModpackVersion != "0.0")
                    server.ModpackVersion = s.ServerModpackVersion;
                if (server.BattleCraftModVersion == "0.0" && s.ServerBattleCraftModVersion != "0.0")
                    server.BattleCraftModVersion = s.ServerBattleCraftModVersion;
                if (server.MapVersion == "0.0" && s.ServerMapVersion != "0.0")
                    server.MapVersion = s.ServerMapVersion;
            }
        }

        public static AppSettings Load()
        {
            lock (_fileLock)
            {
                if (File.Exists(ConfigFile))
                {
                    try
                    {
                        var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(ConfigFile));
                        if (s != null)
                        {
                            if (s.RamMb <= 0) s.RamMb = 4096;
                            MigrateServerVersions(s);
                            return s;
                        }
                    }
                    catch { }
                }
                return new AppSettings();
            }
        }
    }
}
