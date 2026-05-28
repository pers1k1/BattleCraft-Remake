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

            return Directory.GetFiles(serverDirectory, "*.jar", SearchOption.AllDirectories).Length > 0
                || Directory.GetFiles(serverDirectory, "win_args.txt", SearchOption.AllDirectories).Length > 0;
        }

        public async Task InstallAsync(string targetPath, Action<double>? onProgress = null)
        {
            EnsureDirectoryExists(targetPath);

            string serverDirectory = Path.Combine(targetPath, "server");
            string backupDirectory = Path.Combine(targetPath, "backup");

            EnsureDirectoryExists(serverDirectory);
            EnsureDirectoryExists(backupDirectory);

            string temporaryZipPath = Path.Combine(
                Path.GetTempPath(),
                $"bcserver_{Guid.NewGuid():N}.zip");

            string temporaryExtractPath = Path.Combine(
                Path.GetTempPath(),
                $"bcserver_extract_{Guid.NewGuid():N}");

            try
            {
                await DownloadArchive(temporaryZipPath, onProgress);
                await ExtractArchive(temporaryZipPath, temporaryExtractPath);

                string extractedServerDir = Path.Combine(temporaryExtractPath, "server");
                string extractedBackupDir = Path.Combine(temporaryExtractPath, "backup");

                string sourceForCopy = Directory.Exists(extractedServerDir)
                    ? extractedServerDir
                    : temporaryExtractPath;

                await Task.Run(() =>
                {
                    CopyDirectoryContents(sourceForCopy, serverDirectory);

                    if (Directory.Exists(extractedBackupDir))
                        CopyDirectoryContents(extractedBackupDir, backupDirectory);
                    else
                        CopyDirectoryContents(sourceForCopy, backupDirectory);
                });
            }
            finally
            {
                TryDeleteFile(temporaryZipPath);
                TryDeleteDirectory(temporaryExtractPath);
            }
        }

        public static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
        {
            EnsureDirectoryExists(destinationDirectory);

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

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
