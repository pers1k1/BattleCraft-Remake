using System;
using System.IO;
using System.Linq;

namespace CustomLauncher.Core
{
    public static class LauncherLog
    {
        private static readonly object _lock = new object();
        private static string _logFile = "";
        private static string _crashDir = "";
        private const int MaxCrashReports = 2;

        public static void Init()
        {
            try
            {
                string dir = AppSettings.GetConfigDir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                _logFile = Path.Combine(dir, "latest.log");
                _crashDir = Path.Combine(dir, "crash-reports");
                if (!Directory.Exists(_crashDir)) Directory.CreateDirectory(_crashDir);

                CleanupStrayLogs(dir);

                lock (_lock)
                {
                    File.WriteAllText(_logFile, $"[{Stamp()}] [SYS] Лаунчер запущен{Environment.NewLine}");
                }
            }
            catch { }
        }

        public static void Write(string message)
        {
            if (string.IsNullOrEmpty(_logFile)) return;
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_logFile, $"[{Stamp()}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }

        public static string WriteCrash(string context, Exception ex)
        {
            try
            {
                if (string.IsNullOrEmpty(_crashDir))
                {
                    _crashDir = Path.Combine(AppSettings.GetConfigDir(), "crash-reports");
                    if (!Directory.Exists(_crashDir)) Directory.CreateDirectory(_crashDir);
                }

                TrimCrashReports();

                string file = Path.Combine(_crashDir, $"crash-{DateTime.Now:yyyy-MM-dd_HH.mm.ss}.txt");
                string body =
                    $"---- BattleCraft Remake Crash Report ----{Environment.NewLine}" +
                    $"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                    $"Контекст: {context}{Environment.NewLine}{Environment.NewLine}" +
                    $"{ex}{Environment.NewLine}";
                File.WriteAllText(file, body);

                Write($"[ERR] {context}: {ex.Message}");
                return file;
            }
            catch
            {
                return "";
            }
        }

        private static void TrimCrashReports()
        {
            try
            {
                var files = Directory.GetFiles(_crashDir, "crash-*.txt")
                    .OrderBy(f => File.GetCreationTimeUtc(f))
                    .ToList();
                while (files.Count >= MaxCrashReports)
                {
                    try { File.Delete(files[0]); } catch { }
                    files.RemoveAt(0);
                }
            }
            catch { }
        }

        private static void CleanupStrayLogs(string dir)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*.log"))
                {
                    if (!string.Equals(Path.GetFileName(f), "latest.log", StringComparison.OrdinalIgnoreCase))
                        try { File.Delete(f); } catch { }
                }
            }
            catch { }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string old = Path.Combine(baseDir, "crash-log.txt");
                if (File.Exists(old)) try { File.Delete(old); } catch { }
                foreach (var f in Directory.GetFiles(baseDir, "*.jar.log")) try { File.Delete(f); } catch { }
            }
            catch { }
        }

        private static string Stamp() => DateTime.Now.ToString("HH:mm:ss");
    }
}
