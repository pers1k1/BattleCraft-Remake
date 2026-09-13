using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CustomLauncher.Core
{
    public static class VcRedist
    {
        public const string DownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

        private const string RuntimeKey = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";
        private static readonly string[] RequiredLibraries =
        {
            "vcruntime140.dll",
            "vcruntime140_1.dll",
            "msvcp140.dll"
        };

        // WHY: установщик считает успехом три кода: 3010 просит перезагрузку,
        // WHY: 1638 значит, что стоит более новая сборка и ставить нечего
        private static readonly int[] SuccessCodes = { 0, 1638, 3010 };

        public static bool Installed() => RegistryReportsInstalled() || LibrariesPresent();

        public static string InstalledVersion()
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RuntimeKey);
                if (key?.GetValue("Version") is string version) return version.TrimStart('v');
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                LauncherLog.Write($"[WARN] Версия Visual C++ не прочитана: {error.Message}");
            }

            return "";
        }

        public static async Task<bool> InstallAsync(FileDownloader downloader)
        {
            string installer = Path.Combine(Path.GetTempPath(), "bcr_vc_redist_x64.exe");

            try
            {
                await downloader.DownloadFileAsync(DownloadUrl, installer);
            }
            catch (Exception error)
            {
                LauncherLog.Write($"[ERROR] Visual C++ не скачан: {error.Message}");
                return false;
            }

            try
            {
                var start = new ProcessStartInfo(installer, "/install /quiet /norestart")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using Process? process = Process.Start(start);
                if (process == null)
                {
                    LauncherLog.Write("[ERROR] Установщик Visual C++ не запустился");
                    return false;
                }

                await process.WaitForExitAsync();
                int code = process.ExitCode;
                LauncherLog.Write($"[SYS] Установщик Visual C++ завершился с кодом {code}");
                return Array.IndexOf(SuccessCodes, code) >= 0;
            }
            catch (Exception error)
            {
                LauncherLog.Write($"[ERROR] Установка Visual C++ прервана: {error.Message}");
                return false;
            }
            finally
            {
                try { if (File.Exists(installer)) File.Delete(installer); }
                catch (IOException error) { LauncherLog.Write($"[WARN] Установщик Visual C++ остался в temp: {error.Message}"); }
            }
        }

        private static bool RegistryReportsInstalled()
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RuntimeKey);
                return key?.GetValue("Installed") is int installed && installed == 1;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                LauncherLog.Write($"[WARN] Ветка Visual C++ не прочитана: {error.Message}");
                return false;
            }
        }

        private static bool LibrariesPresent()
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);

            foreach (string library in RequiredLibraries)
            {
                if (!File.Exists(Path.Combine(system, library))) return false;
            }

            return true;
        }
    }
}
