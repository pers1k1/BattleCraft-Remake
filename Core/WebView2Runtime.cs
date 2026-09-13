using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CustomLauncher.Core
{
    public static class WebView2Runtime
    {
        public const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
        public const string ManualPage = "https://developer.microsoft.com/microsoft-edge/webview2/";

        private const string ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

        public static bool Installed() => !string.IsNullOrEmpty(Version());

        public static string Version()
        {
            string[] machineKeys =
            {
                $@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{ClientGuid}",
                $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{ClientGuid}"
            };

            foreach (string key in machineKeys)
            {
                string found = Read(Registry.LocalMachine, key);
                if (!string.IsNullOrEmpty(found)) return found;
            }

            return Read(Registry.CurrentUser, $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{ClientGuid}");
        }

        public static async Task<bool> InstallAsync()
        {
            string installer = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");

            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
                {
                    byte[] bytes = await http.GetByteArrayAsync(DownloadUrl);
                    await File.WriteAllBytesAsync(installer, bytes);
                }

                var start = new ProcessStartInfo(installer, "/silent /install") { UseShellExecute = true };
                using Process? process = Process.Start(start);
                if (process != null) await process.WaitForExitAsync();

                bool installed = Installed();
                LauncherLog.Write($"[SYS] WebView2 после установки: {(installed ? Version() : "не найден")}");
                return installed;
            }
            catch (Exception error)
            {
                LauncherLog.Write($"[ERROR] WebView2 не установлен: {error.Message}");
                return false;
            }
        }

        private static string Read(RegistryKey root, string path)
        {
            try
            {
                using RegistryKey? key = root.OpenSubKey(path);
                return key?.GetValue("pv") is string version && version != "0.0.0.0" ? version : "";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                LauncherLog.Write($"[WARN] Ветка WebView2 не прочитана: {error.Message}");
                return "";
            }
        }
    }
}
