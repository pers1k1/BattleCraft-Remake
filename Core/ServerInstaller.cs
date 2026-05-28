using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace CustomLauncher.Core
{
    public class ServerInstaller
    {
        private const string SERVER_ARCHIVE_URL =
            "https://github.com/pers1k1/server/releases/download/main/server.zip";

        public static bool IsInstalled(string serverBasePath)
        {
            string serverDirectory = Path.Combine(serverBasePath, "server");

            if (!Directory.Exists(serverDirectory))
                return false;

            return Directory.GetFiles(serverDirectory, "*.jar").Length > 0;
        }

        public async Task InstallAsync(string targetPath, Action<double>? onProgress = null)
        {
            EnsureDirectoryExists(targetPath);

            string temporaryZipPath = Path.Combine(
                Path.GetTempPath(),
                $"bcserver_{Guid.NewGuid():N}.zip");

            try
            {
                await DownloadArchive(temporaryZipPath, onProgress);
                await ExtractArchive(temporaryZipPath, targetPath);
            }
            finally
            {
                TryDeleteFile(temporaryZipPath);
            }
        }

        public async Task RestoreBackupAsync(string serverBasePath, Action<double>? onProgress = null)
        {
            string backupDirectory = Path.Combine(serverBasePath, "backup");
            string serverDirectory = Path.Combine(serverBasePath, "server");

            bool backupMissing = !Directory.Exists(backupDirectory)
                || Directory.GetFileSystemEntries(backupDirectory).Length == 0;

            if (backupMissing)
            {
                await InstallAsync(serverBasePath, onProgress);
                return;
            }

            await Task.Run(() => CopyDirectoryRecursive(backupDirectory, serverDirectory));
        }

        private static async Task DownloadArchive(string destinationPath, Action<double>? onProgress)
        {
            var downloader = new FileDownloader();

            if (onProgress != null)
                downloader.ProgressChanged += onProgress;

            await downloader.DownloadFileAsync(SERVER_ARCHIVE_URL, destinationPath);
        }

        private static async Task ExtractArchive(string zipPath, string targetDirectory)
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, targetDirectory, true));
        }

        private static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory)
        {
            foreach (string directoryPath in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string targetSubDirectory = directoryPath.Replace(sourceDirectory, destinationDirectory);
                EnsureDirectoryExists(targetSubDirectory);
            }

            foreach (string filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string targetFilePath = filePath.Replace(sourceDirectory, destinationDirectory);
                File.Copy(filePath, targetFilePath, overwrite: true);
            }
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
