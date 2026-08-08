using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CustomLauncher.Core
{
    public static class UpdateResidue
    {
        public const string CleanupPidOption = "--cleanup-pid";
        public const string StagedUpdateName = "upd.exe";

        private const string BackupExtension = ".old";
        private const string StaleMarkerPrefix = "bcr-stale-";
        private const string StaleMarkerExtension = ".tmp";
        private const int DeleteAttempts = 60;
        private const int DeleteRetryDelayMs = 250;
        private const int ReplacedInstanceWaitSeconds = 30;
        private const int MaxNumberedBackups = 64;

        public static string RelaunchArguments(int replacedProcessId) => $"{CleanupPidOption} {replacedProcessId}";

        public static string ReserveBackupPath(string executablePath)
        {
            string preferred = executablePath + BackupExtension;
            if (TryDelete(preferred)) return preferred;

            for (int index = 1; index < MaxNumberedBackups; index++)
            {
                string numbered = $"{executablePath}.{index}{BackupExtension}";
                if (TryDelete(numbered)) return numbered;
            }

            return $"{executablePath}.{Guid.NewGuid():N}{BackupExtension}";
        }

        public static async Task PurgeAsync(IReadOnlyList<string> startupArguments)
        {
            await WaitForReplacedInstanceAsync(startupArguments);

            foreach (string residue in FindResidue(LauncherDirectory()))
                await EraseAsync(residue);
        }

        private static string LauncherDirectory()
        {
            string? processDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? "");
            return string.IsNullOrEmpty(processDirectory) ? AppContext.BaseDirectory : processDirectory;
        }

        private static async Task WaitForReplacedInstanceAsync(IReadOnlyList<string> startupArguments)
        {
            if (!TryReadReplacedProcessId(startupArguments, out int processId)) return;

            try
            {
                using var replaced = Process.GetProcessById(processId);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ReplacedInstanceWaitSeconds));
                await replaced.WaitForExitAsync(timeout.Token);
            }
            catch { }
        }

        private static bool TryReadReplacedProcessId(IReadOnlyList<string> startupArguments, out int processId)
        {
            processId = 0;
            for (int index = 0; index + 1 < startupArguments.Count; index++)
                if (CleanupPidOption.Equals(startupArguments[index], StringComparison.OrdinalIgnoreCase))
                    return int.TryParse(startupArguments[index + 1], out processId);

            return false;
        }

        private static IEnumerable<string> FindResidue(string directory)
        {
            var residue = new List<string>();
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                    if (IsResidue(Path.GetFileName(file))) residue.Add(file);
            }
            catch { }
            return residue;
        }

        private static bool IsResidue(string fileName) =>
            fileName.EndsWith(BackupExtension, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(StagedUpdateName, StringComparison.OrdinalIgnoreCase)
            || IsStaleMarker(fileName);

        private static bool IsStaleMarker(string fileName) =>
            fileName.StartsWith(StaleMarkerPrefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(StaleMarkerExtension, StringComparison.OrdinalIgnoreCase);

        private static async Task EraseAsync(string path)
        {
            for (int attempt = 0; attempt < DeleteAttempts; attempt++)
            {
                if (TryDelete(path)) return;
                await Task.Delay(DeleteRetryDelayMs);
            }

            Quarantine(path);
        }

        private static void Quarantine(string path)
        {
            if (TryMove(path, Path.Combine(Path.GetTempPath(), StaleMarkerName()), out string moved))
            {
                TryDelete(moved);
                return;
            }

            string directory = Path.GetDirectoryName(path) ?? LauncherDirectory();
            if (TryMove(path, Path.Combine(directory, StaleMarkerName()), out string hidden)) Hide(hidden);
        }

        private static string StaleMarkerName() => $"{StaleMarkerPrefix}{Guid.NewGuid():N}{StaleMarkerExtension}";

        private static bool TryMove(string path, string destination, out string moved)
        {
            moved = destination;
            try
            {
                File.Move(path, destination);
                return true;
            }
            catch { return false; }
        }

        private static void Hide(string path)
        {
            try { File.SetAttributes(path, FileAttributes.Hidden); } catch { }
        }

        private static bool TryDelete(string path)
        {
            try
            {
                if (!File.Exists(path)) return true;
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return !File.Exists(path);
            }
            catch { return false; }
        }
    }
}
