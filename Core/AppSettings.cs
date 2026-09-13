using System;
using System.Collections.Generic;
using System.IO;
using CustomLauncher.Core;
using Newtonsoft.Json;

namespace CustomLauncher
{
    public class AppSettings
    {
        private const string FolderName = "CustomLauncher";
        private const string FileName = "launcher_config.json";

        private static readonly object _fileLock = new object();
        private static readonly object _dirLock = new object();
        private static string? _configDir;

        public static string ConfigDirNotice { get; private set; } = "";

        private static string ConfigFile => Path.Combine(GetConfigDir(), FileName);

        public string Language { get; set; } = "";
        public string? PrimaryColor { get; set; } = "#14101A";
        public string? AccentColor { get; set; } = "#F7CAD0";
        public bool? BloomEnabled { get; set; } = true;
        public double? BloomStrength { get; set; } = 60.0;
        public double? ConsoleOpacity { get; set; } = 1.0;
        public string BackgroundMode { get; set; } = "animated";
        public List<CustomPreset> CustomPresets { get; set; } = new();
        public string Username { get; set; } = "";
        public string UserType { get; set; } = "";
        public int RamMb { get; set; } = 4096;
        public string GamePath { get; set; } = "";
        public bool IsModpackInstalled { get; set; } = false;
        public bool DebugConsole { get; set; } = false;
        public int DownloadLanes { get; set; } = 0;
        public bool SafeJvm { get; set; } = false;
        public string ModpackVersion { get; set; } = "0.0";
        public string BattleCraftModVersion { get; set; } = "0.0";
        public string ServerModpackVersion { get; set; } = "0.0";
        public string ServerBattleCraftModVersion { get; set; } = "0.0";
        public string ServerMapVersion { get; set; } = "0.0";
        public List<ServerConfig> Servers { get; set; } = new();
        public string LastActiveServerName { get; set; } = "";

        public bool IsFirstRun => string.IsNullOrWhiteSpace(Username);
        public bool HasGamePath => !string.IsNullOrWhiteSpace(GamePath);

        // WHY: у части игроков папка «Документы» перенаправлена в облако или закрыта защитой
        // WHY: папок антивируса, и лаунчер молча терял и настройки, и лог; тогда уходим в AppData
        public static string GetConfigDir()
        {
            lock (_dirLock)
            {
                if (_configDir != null) return _configDir;

                string primary = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), FolderName);
                string[] candidates =
                {
                    primary,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName),
                    Path.Combine(AppContext.BaseDirectory, FolderName)
                };

                foreach (string candidate in candidates)
                {
                    if (!Prepare(candidate, out string error))
                    {
                        if (candidate == primary) ConfigDirNotice = primary + ": " + error;
                        continue;
                    }

                    if (candidate != primary) AdoptOldConfig(primary, candidate);

                    _configDir = candidate;
                    return _configDir;
                }

                _configDir = primary;
                return _configDir;
            }
        }

        private static bool Prepare(string path, out string error)
        {
            error = "";
            string probe = Path.Combine(path, "bcr_write_test.tmp");

            try
            {
                Directory.CreateDirectory(path);
                File.WriteAllText(probe, "bcr");
                File.Delete(probe);
                return true;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                error = failure.Message;
                return false;
            }
        }

        private static void AdoptOldConfig(string primary, string target)
        {
            string source = Path.Combine(primary, FileName);
            string destination = Path.Combine(target, FileName);

            try
            {
                if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ConfigDirNotice += " | " + error.Message;
            }
        }

        public static bool Save(AppSettings settings)
        {
            lock (_fileLock)
            {
                string directory = GetConfigDir();
                string file = Path.Combine(directory, FileName);

                try
                {
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(file, JsonConvert.SerializeObject(settings, Formatting.Indented));
                    return true;
                }
                catch (Exception error)
                {
                    LauncherLog.Write("[ERROR] Настройки лаунчера не сохранены в " + file + ": " + error.Message);
                    return false;
                }
            }
        }

        // WHY: битый конфиг иначе молча заменялся пустым, и игрок терял папку игры,
        // WHY: вход и список серверов, не увидев ни одного сообщения
        private static void KeepBroken(string file)
        {
            try
            {
                string copy = file + ".broken";
                File.Copy(file, copy, true);
                LauncherLog.Write("[SYS] Прежний конфиг сохранён как " + copy);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LauncherLog.Write("[WARN] Копия битого конфига не сделана: " + error.Message);
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
                string file = ConfigFile;
                if (File.Exists(file))
                {
                    try
                    {
                        var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(file));
                        if (s != null)
                        {
                            if (s.RamMb <= 0) s.RamMb = 4096;
                            MigrateServerVersions(s);
                            return s;
                        }

                        LauncherLog.Write("[ERROR] Настройки лаунчера пусты, файл " + file + " будет перезаписан");
                    }
                    catch (Exception error)
                    {
                        LauncherLog.Write("[ERROR] Настройки лаунчера не прочитаны из " + file + ": " + error.Message);
                        KeepBroken(file);
                    }
                }
                return new AppSettings();
            }
        }
    }
}
