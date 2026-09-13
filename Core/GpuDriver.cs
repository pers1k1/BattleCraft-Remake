using System;
using System.IO;
using Microsoft.Win32;

namespace CustomLauncher.Core
{
    public sealed class GpuDriver
    {
        private const string AdaptersKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        public const string NvidiaDownloads = "https://www.nvidia.com/Download/index.aspx";
        public const string AmdDownloads = "https://www.amd.com/en/support";
        public const string IntelDownloads = "https://www.intel.com/content/www/us/en/download-center/home.html";
        public const string WindowsUpdate = "https://support.microsoft.com/windows/update-drivers-manually-in-windows-ec62f46c-ff14-c91d-eead-d7126dc1f7b6";

        public string Name { get; private init; } = "";
        public string Version { get; private init; } = "";
        public DateTime? Released { get; private init; }

        public string DownloadUrl => Name.ToUpperInvariant() switch
        {
            var name when name.Contains("NVIDIA") || name.Contains("GEFORCE") => NvidiaDownloads,
            var name when name.Contains("AMD") || name.Contains("RADEON") => AmdDownloads,
            var name when name.Contains("INTEL") => IntelDownloads,
            _ => WindowsUpdate
        };

        public string Describe()
        {
            if (string.IsNullOrEmpty(Name)) return "";
            if (string.IsNullOrEmpty(Version)) return Name;

            string age = Released.HasValue ? $", {Released.Value:dd.MM.yyyy}" : "";
            return $"{Name} ({Version}{age})";
        }

        public static GpuDriver Read()
        {
            try
            {
                using RegistryKey? adapters = Registry.LocalMachine.OpenSubKey(AdaptersKey);
                if (adapters == null) return new GpuDriver();

                foreach (string name in adapters.GetSubKeyNames())
                {
                    if (name.Length != 4 || !int.TryParse(name, out _)) continue;

                    using RegistryKey? adapter = adapters.OpenSubKey(name);
                    if (adapter?.GetValue("DriverDesc") is not string description) continue;

                    return new GpuDriver
                    {
                        Name = description,
                        Version = adapter.GetValue("DriverVersion") as string ?? "",
                        Released = ParseDate(adapter.GetValue("DriverDate") as string)
                    };
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                LauncherLog.Write($"[WARN] Видеодрайвер не определён: {error.Message}");
            }

            return new GpuDriver();
        }

        private static DateTime? ParseDate(string? raw)
        {
            return DateTime.TryParse(raw, out DateTime parsed) ? parsed : null;
        }
    }
}
