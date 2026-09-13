using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CustomLauncher.Core
{
    public static class GameLogTail
    {
        // WHY: в обычном логе полно безобидных ERROR про висящие картины, поэтому сначала
        // WHY: показываем настоящие поломки и только при их отсутствии откатываемся на общие
        private static readonly string[] StrongMarkers = { "FATAL", "Exception", "Caused by", "crash report" };
        private static readonly string[] WeakMarkers = { "/ERROR]", "Could not", "Failed to" };

        public static string LatestLogPath(string gamePath) => Path.Combine(gamePath, "logs", "latest.log");

        public static string NewestCrashReport(string gamePath, TimeSpan age)
        {
            string folder = Path.Combine(gamePath, "crash-reports");
            if (!Directory.Exists(folder)) return "";

            try
            {
                DateTime border = DateTime.Now - age;
                return Directory.GetFiles(folder, "crash-*.txt")
                    .Select(path => new FileInfo(path))
                    .Where(file => file.LastWriteTime >= border)
                    .OrderByDescending(file => file.LastWriteTime)
                    .Select(file => file.FullName)
                    .FirstOrDefault() ?? "";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LauncherLog.Write($"[WARN] Отчёты игры не прочитаны: {error.Message}");
                return "";
            }
        }

        // WHY: звуковой движок стартует ровно на титульном экране, и это единственный
        // WHY: надёжный признак, что игра дожила до меню, а не умерла на загрузке
        public static bool ReachedMenu(string gamePath)
        {
            string path = LatestLogPath(gamePath);
            if (!File.Exists(path)) return false;

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                while (reader.ReadLine() is { } line)
                {
                    if (line.Contains("Sound engine started", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LauncherLog.Write($"[WARN] Лог игры не прочитан: {error.Message}");
            }

            return false;
        }

        public static List<string> Problems(string gamePath, int maxLines)
        {
            var strong = new List<string>();
            var weak = new List<string>();
            string path = LatestLogPath(gamePath);
            if (!File.Exists(path)) return strong;

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                while (reader.ReadLine() is { } line)
                {
                    List<string>? bucket = Matches(line, StrongMarkers) ? strong
                        : Matches(line, WeakMarkers) ? weak
                        : null;
                    if (bucket == null) continue;

                    bucket.Add(line.Length > 200 ? line[..200] + "…" : line);
                    if (bucket.Count > maxLines) bucket.RemoveAt(0);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LauncherLog.Write($"[WARN] Лог игры не прочитан: {error.Message}");
            }

            return strong.Count > 0 ? strong : weak;
        }

        // WHY: Forge сыплет «Error loading class» на каждый миксин чужого загрузчика,
        // WHY: и без этого фильтра сообщение об ошибке состоит из одного этого шума
        private const string MixinNoise = "Error loading class";

        private static bool Matches(string line, string[] markers) =>
            !line.Contains(MixinNoise, StringComparison.OrdinalIgnoreCase)
            && markers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
