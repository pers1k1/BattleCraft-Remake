using System;
using System.IO;
using Newtonsoft.Json;

namespace CustomLauncher
{
    public class AppSettings
    {
        public static readonly string DefaultGamePath = "";

        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CustomLauncher");

        private static readonly string ConfigFile = Path.Combine(ConfigDir, "launcher_config.json");

        public string? PrimaryColor { get; set; } = "#0D0D1E";
        public string? AccentColor { get; set; } = "#BB86FC";
        public bool? BloomEnabled { get; set; } = true;
        public double? BloomStrength { get; set; } = 60.0;
        public string Username { get; set; } = "";
        public int RamMb { get; set; } = 4096;
        public string GamePath { get; set; } = "";
        public string JavaPath { get; set; } = "";
        public bool IsModpackInstalled { get; set; } = false;
        public bool DebugConsole { get; set; } = false;
        public bool ParticlesEnabled { get; set; } = false;
        public string ModpackVersion { get; set; } = "0.0";

        public bool IsFirstRun => string.IsNullOrWhiteSpace(Username);
        public bool HasGamePath => !string.IsNullOrWhiteSpace(GamePath);

        public static string GetConfigDir() => ConfigDir;

        public static void Save(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch { }
        }

        public static AppSettings Load()
        {
            if (File.Exists(ConfigFile))
            {
                try
                {
                    var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(ConfigFile));
                    if (s != null) { if (s.RamMb <= 0) s.RamMb = 4096; return s; }
                }
                catch { }
            }
            return new AppSettings();
        }
    }
}
