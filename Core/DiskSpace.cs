using System;
using System.Globalization;
using System.IO;

namespace CustomLauncher.Core
{
    public static class DiskSpace
    {
        public const long ClientRequiredBytes = 6L * 1024 * 1024 * 1024;
        public const long ServerRequiredBytes = 4L * 1024 * 1024 * 1024;

        public static bool TryGetFreeBytes(string path, out long freeBytes)
        {
            freeBytes = 0;

            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root))
                    return false;

                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                    return false;

                freeBytes = drive.AvailableFreeSpace;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool HasEnough(string path, long requiredBytes, out long freeBytes)
        {
            return !TryGetFreeBytes(path, out freeBytes) || freeBytes >= requiredBytes;
        }

        public static string Describe(long bytes)
        {
            double gigabytes = bytes / 1024d / 1024d / 1024d;

            return gigabytes >= 1
                ? gigabytes.ToString("0.#", CultureInfo.CurrentCulture) + " ГБ"
                : Math.Round(bytes / 1024d / 1024d).ToString(CultureInfo.CurrentCulture) + " МБ";
        }

        public static string BuildShortageMessage(string path, long requiredBytes, long freeBytes)
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));

            return $"Не хватает места на диске {root}\n\n"
                + $"Нужно: {Describe(requiredBytes)}\n"
                + $"Свободно: {Describe(freeBytes)}\n\n"
                + "Освободите место или выберите другой диск.";
        }
    }
}
