using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CustomLauncher.Core
{
    public static class GamePathRules
    {
        public const int LongPathLimit = 90;
        public const string FolderName = "BattleCraft";
        public const string AllowedMessage = "В пути допустимы только латинские буквы, цифры, пробел и знаки _ - . ( )";

        private const int SpareFolderCount = 9;

        private static readonly HashSet<string> ReservedNames = BuildReservedNames();

        public static bool Allows(char symbol) =>
            char.IsAsciiLetterOrDigit(symbol) || symbol is ' ' or '_' or '-' or '.' or '(' or ')';

        public static bool IsSafe(string? path) => Problems(path).Count == 0;

        public static List<string> Problems(string? path)
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(path)) return problems;

            if (IsNetworkPath(path))
            {
                problems.Add(Lang.T("это сетевой путь - Java и Forge не загружают сборку по сети"));
                return problems;
            }

            if (!HasDriveRoot(path))
            {
                problems.Add(Lang.T("путь неполный, нужен вид C:\\Папка"));
                return problems;
            }

            List<string> folders = Folders(path);
            AddForeignLetterProblem(folders, problems);
            AddSpecialSymbolProblem(folders, problems);
            AddReservedNameProblems(folders, problems);

            if (path.Length > LongPathLimit)
                problems.Add(Lang.F("путь длиннее {0} символов - распаковка модов обрывается на длинных именах", LongPathLimit));

            return problems;
        }

        public static string? SuggestFolder(string? currentPath)
        {
            foreach (string root in CandidateRoots(currentPath))
            {
                string? folder = FreeFolderOn(root);
                if (folder != null) return folder;
            }

            return null;
        }

        public static bool HoldsFiles(string path)
        {
            try { return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any(); }
            catch (Exception error)
            {
                LauncherLog.Write($"[WARN] Содержимое {path} не прочитано: {error.Message}");
                return true;
            }
        }

        public static bool SameDrive(string first, string second) =>
            string.Equals(RootOf(first), RootOf(second), StringComparison.OrdinalIgnoreCase);

        public static bool IsInside(string path, string folder)
        {
            string inner = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
            string outer = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            return inner.StartsWith(outer, StringComparison.OrdinalIgnoreCase);
        }

        public static void MoveInstall(string from, string to)
        {
            string? parent = Path.GetDirectoryName(to);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            if (Directory.Exists(to)) Directory.Delete(to);
            Directory.Move(from, to);
        }

        public static string Rebase(string path, string oldRoot, string newRoot)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsInside(path, oldRoot)) return path;

            string relative = Path.GetRelativePath(oldRoot, path);
            return relative == "." ? newRoot : Path.Combine(newRoot, relative);
        }

        private static bool IsNetworkPath(string path) =>
            path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

        private static bool HasDriveRoot(string path) =>
            path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

        private static bool IsSeparator(char symbol) => symbol == '\\' || symbol == '/';

        private static List<string> Folders(string path) =>
            path.Substring(3).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        private static void AddForeignLetterProblem(List<string> folders, List<string> problems)
        {
            List<string> foreign = folders.Where(folder => folder.Any(symbol => symbol > 127)).Distinct().ToList();
            if (foreign.Count == 0) return;

            problems.Add(Lang.F("не латиница в {0} - Java и Forge спотыкаются о такие буквы при загрузке модов",
                string.Join(", ", foreign.Select(folder => "«" + folder + "»"))));
        }

        private static void AddSpecialSymbolProblem(List<string> folders, List<string> problems)
        {
            List<char> special = folders
                .SelectMany(folder => folder)
                .Where(symbol => symbol <= 127 && !Allows(symbol) && !IsSeparator(symbol))
                .Distinct()
                .ToList();
            if (special.Count == 0) return;

            problems.Add(Lang.F("недопустимые знаки {0} - часть из них (! # % ; +) Java читает как служебные в путях к jar и classpath",
                string.Join(" ", special.Select(DescribeSymbol))));
        }

        private static void AddReservedNameProblems(List<string> folders, List<string> problems)
        {
            foreach (string folder in folders.Distinct())
            {
                string device = folder.Split('.')[0].TrimEnd().ToUpperInvariant();
                if (ReservedNames.Contains(device) || folder.EndsWith('.') || folder.EndsWith(' '))
                    problems.Add(Lang.F("имя папки «{0}» Windows не допускает", folder));
            }
        }

        private static string DescribeSymbol(char symbol) =>
            char.IsControl(symbol) ? $"U+{(int)symbol:X4}" : symbol.ToString();

        private static HashSet<string> BuildReservedNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal) { "CON", "PRN", "AUX", "NUL" };
            for (int index = 0; index <= 9; index++)
            {
                names.Add("COM" + index);
                names.Add("LPT" + index);
            }

            return names;
        }

        private static string? RootOf(string? path) =>
            !string.IsNullOrEmpty(path) && HasDriveRoot(path) ? char.ToUpperInvariant(path[0]) + @":\" : null;

        private static IEnumerable<string> CandidateRoots(string? currentPath)
        {
            var roots = new List<string>();
            string? current = RootOf(currentPath);
            if (current != null) roots.Add(current);

            string? system = RootOf(Environment.SystemDirectory);
            if (system != null) roots.Add(system);

            roots.AddRange(LocalDrives().OrderByDescending(drive => drive.Free).Select(drive => drive.Root));

            return roots.Distinct(StringComparer.OrdinalIgnoreCase).Where(IsLocalDrive);
        }

        private static List<(string Root, long Free)> LocalDrives()
        {
            var drives = new List<(string Root, long Free)>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        drives.Add((drive.RootDirectory.FullName, drive.AvailableFreeSpace));
                }
                catch (Exception error)
                {
                    LauncherLog.Write($"[WARN] Диск {drive.Name} не опрошен: {error.Message}");
                }
            }

            return drives;
        }

        private static bool IsLocalDrive(string root)
        {
            try
            {
                var drive = new DriveInfo(root);
                return drive.IsReady && drive.DriveType == DriveType.Fixed;
            }
            catch (Exception) { return false; }
        }

        private static string? FreeFolderOn(string root)
        {
            foreach (string folder in FolderCandidates(root))
            {
                if (File.Exists(folder) || HoldsFiles(folder)) continue;
                return CanCreate(folder) ? folder : null;
            }

            return null;
        }

        private static IEnumerable<string> FolderCandidates(string root)
        {
            yield return Path.Combine(root, FolderName);
            for (int index = 2; index <= SpareFolderCount; index++)
                yield return Path.Combine(root, FolderName + index, FolderName);
        }

        private static bool CanCreate(string folder)
        {
            string? created = TopmostMissing(folder);
            try
            {
                Directory.CreateDirectory(folder);
                string probe = Path.Combine(folder, "bcr_path_probe.tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception error)
            {
                LauncherLog.Write($"[WARN] Папку {folder} создать нельзя: {error.Message}");
                return false;
            }
            finally
            {
                if (created != null) RemoveProbeFolder(created);
            }
        }

        private static string? TopmostMissing(string folder)
        {
            string? missing = null;
            for (string? current = folder; !string.IsNullOrEmpty(current) && !Directory.Exists(current); current = Path.GetDirectoryName(current))
                missing = current;

            return missing;
        }

        private static void RemoveProbeFolder(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (Exception error) { LauncherLog.Write($"[WARN] Пробная папка {folder} не удалена: {error.Message}"); }
        }
    }
}
