using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace CustomLauncher.Core
{
    public class ServerInstaller
    {
        private const string FORGE_INSTALLER_URL =
            "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-installer.jar";

        private const string SERVER_DATA_ARCHIVE_URL =
            "https://github.com/pers1k1/server/releases/download/main/server.zip";

        private const string SERVER_MODS_ARCHIVE_URL =
            "https://github.com/pers1k1/server/releases/download/main/mods_server.zip";

        public event Action<string>? StatusChanged;
        public event Action<string>? LogReceived;

        public static bool IsInstalled(string serverBasePath)
        {
            string serverDirectory = Path.Combine(serverBasePath, "server");

            if (!Directory.Exists(serverDirectory))
                return false;

            bool hasArgsFile = Directory.GetFiles(serverDirectory, "win_args.txt", SearchOption.AllDirectories).Length > 0
                            || Directory.GetFiles(serverDirectory, "unix_args.txt", SearchOption.AllDirectories).Length > 0;

            bool hasForgeJar = Directory.GetFiles(serverDirectory, "forge-*.jar", SearchOption.AllDirectories).Length > 0;

            return hasArgsFile || hasForgeJar;
        }

        public async Task InstallAsync(string targetPath, string javaPath, Action<double>? onProgress = null)
        {
            EnsureDirectoryExists(targetPath);

            string serverDirectory = Path.Combine(targetPath, "server");
            string backupDirectory = Path.Combine(targetPath, "backup");

            EnsureDirectoryExists(serverDirectory);
            EnsureDirectoryExists(backupDirectory);

            await InstallForgeRuntime(serverDirectory, javaPath, onProgress);
            await DownloadAndApplyServerData(serverDirectory, backupDirectory, onProgress);
            await UpdateServerMods(serverDirectory, onProgress);
        }

        public async Task UpdateServerMods(string serverDirectory, Action<double>? onProgress = null)
        {
            string temporaryZipPath = Path.Combine(serverDirectory, $"mods_server_{Guid.NewGuid():N}.zip");

            try
            {
                ReportStatus("Скачивание модов сервера...");
                await DownloadFile(SERVER_MODS_ARCHIVE_URL, temporaryZipPath, onProgress);

                ReportStatus("Очистка старых модов...");
                TryDeleteDirectory(Path.Combine(serverDirectory, "mods"));
                TryDeleteDirectory(Path.Combine(serverDirectory, "tacz"));
                TryDeleteDirectory(Path.Combine(serverDirectory, "tacz_backup"));

                ReportStatus("Распаковка модов сервера...");
                await Task.Run(() => ZipFile.ExtractToDirectory(temporaryZipPath, serverDirectory, true));
            }
            finally
            {
                TryDeleteFile(temporaryZipPath);
            }
        }

        public async Task InstallForgeRuntime(string serverDirectory, string javaPath, Action<double>? onProgress)
        {
            string installerJarPath = Path.Combine(
                Path.GetTempPath(),
                $"forge_server_installer_{Guid.NewGuid():N}.jar");

            try
            {
                ReportStatus("Скачивание Forge installer...");
                await DownloadFile(FORGE_INSTALLER_URL, installerJarPath, onProgress);

                ReportStatus("Установка Forge сервера...");
                await RunForgeInstaller(installerJarPath, serverDirectory, javaPath);
            }
            finally
            {
                TryDeleteFile(installerJarPath);
            }
        }

        private async Task RunForgeInstaller(string installerJarPath, string serverDirectory, string javaPath)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = $"-jar \"{installerJarPath}\" --installServer",
                WorkingDirectory = serverDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo)
                ?? throw new InvalidOperationException("Java process failed to start.");

            process.OutputDataReceived += OnInstallerOutputReceived;
            process.ErrorDataReceived += OnInstallerOutputReceived;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Forge installer exited with code {process.ExitCode}");

            CleanInstallerArtifacts(serverDirectory);
        }

        private void OnInstallerOutputReceived(object sender, DataReceivedEventArgs eventArgs)
        {
            if (eventArgs.Data != null)
                LogReceived?.Invoke(eventArgs.Data);
        }

        private static void CleanInstallerArtifacts(string serverDirectory)
        {
            string? parentDirectory = Directory.GetParent(serverDirectory)?.FullName;

            if (parentDirectory != null)
            {
                foreach (string logFile in Directory.GetFiles(parentDirectory, "installer*.log", SearchOption.TopDirectoryOnly))
                    TryDeleteFile(logFile);
            }

            foreach (string logFile in Directory.GetFiles(serverDirectory, "installer*.log", SearchOption.TopDirectoryOnly))
                TryDeleteFile(logFile);
        }

        public async Task DownloadAndApplyServerData(
            string serverDirectory, string backupDirectory, Action<double>? onProgress)
        {
            string temporaryZipPath = Path.Combine(
                Path.GetTempPath(),
                $"bcserver_data_{Guid.NewGuid():N}.zip");

            string temporaryExtractPath = Path.Combine(
                Path.GetTempPath(),
                $"bcserver_data_extract_{Guid.NewGuid():N}");

            try
            {
                ReportStatus("Скачивание данных сервера...");
                await DownloadFile(SERVER_DATA_ARCHIVE_URL, temporaryZipPath, onProgress);

                ReportStatus("Распаковка данных...");
                await Task.Run(() => ZipFile.ExtractToDirectory(temporaryZipPath, temporaryExtractPath, true));

                ReportStatus("Копирование в server...");
                await Task.Run(() => CopyDirectoryContents(temporaryExtractPath, serverDirectory));

                ReportStatus("Копирование в backup...");
                await Task.Run(() => CopyDirectoryContents(temporaryExtractPath, backupDirectory));
            }
            finally
            {
                TryDeleteFile(temporaryZipPath);
                TryDeleteDirectory(temporaryExtractPath);
            }
        }

        public async Task UpdateServerMap(string serverDirectory, string backupDirectory, Action<double>? onProgress)
        {
            await DownloadAndApplyServerData(serverDirectory, backupDirectory, onProgress);
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

        private async Task DownloadFile(string url, string destinationPath, Action<double>? onProgress)
        {
            var downloader = new FileDownloader();

            if (onProgress != null)
                downloader.ProgressChanged += onProgress;

            await downloader.DownloadFileAsync(url, destinationPath);
        }

        private void ReportStatus(string message)
        {
            StatusChanged?.Invoke(message);
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
