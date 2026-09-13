using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace CustomLauncher.Core
{
    // WHY: обрыв закачки оставляет обрезанный jar, и Forge падает на нём через SecureJar за
    // WHY: секунду до своего лога; библиотеки самого Forge кладёт его установщик, поэтому
    // WHY: перекачать их через манифест нельзя - битые файлы надо найти и снести самим
    public static class BrokenJars
    {
        public static readonly string[] Folders = { "libraries", "versions", "mods" };

        public static List<string> Find(string gamePath)
        {
            var broken = new List<string>();

            foreach (string folder in Folders)
            {
                string path = Path.Combine(gamePath, folder);
                if (!Directory.Exists(path)) continue;

                try
                {
                    foreach (string jar in Directory.EnumerateFiles(path, "*.jar", SearchOption.AllDirectories))
                    {
                        if (!Readable(jar)) broken.Add(jar);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    LauncherLog.Write($"[WARN] Папка {folder} не просмотрена на битые файлы: {error.Message}");
                }
            }

            return broken;
        }

        public static int Remove(IEnumerable<string> jars)
        {
            int removed = 0;

            foreach (string jar in jars)
            {
                try
                {
                    File.Delete(jar);
                    removed++;
                    LauncherLog.Write($"[SYS] Удалён повреждённый файл {jar}");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    LauncherLog.Write($"[WARN] Повреждённый файл {jar} не удалён: {error.Message}");
                }
            }

            return removed;
        }

        private static bool Readable(string jar)
        {
            try
            {
                var info = new FileInfo(jar);
                if (info.Length == 0) return false;

                using ZipArchive archive = ZipFile.OpenRead(jar);
                return archive.Entries.Count > 0;
            }
            catch (InvalidDataException)
            {
                return false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LauncherLog.Write($"[WARN] Файл {jar} не проверен: {error.Message}");
                return true;
            }
        }
    }
}
