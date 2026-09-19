using System;
using System.IO;
using System.Linq;

namespace CustomLauncher.Core
{
    public static class JavaRuntime
    {
        public const string SystemJava = "java";

        // WHY: CmlLib раскладывает рантайм как runtime/windows-x64/<компонент>, а официальный
        // WHY: лаунчер Mojang - как runtime/<компонент>/windows-x64/<компонент>; встречаются обе
        public static string BundledFolder(string gamePath) =>
            Path.Combine(gamePath, "runtime", "windows-x64", GameVersions.JavaRuntimeComponent);

        private static string MojangFolder(string gamePath) =>
            Path.Combine(gamePath, "runtime", GameVersions.JavaRuntimeComponent, "windows-x64", GameVersions.JavaRuntimeComponent);

        public static bool IsBundled(string javaPath) =>
            !string.Equals(javaPath, SystemJava, StringComparison.OrdinalIgnoreCase) && File.Exists(javaPath);

        public static string Find(string gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath)) return SystemJava;

            foreach (string folder in new[] { BundledFolder(gamePath), MojangFolder(gamePath) })
            {
                string candidate = Path.Combine(folder, "bin", "java.exe");
                if (File.Exists(candidate)) return candidate;
            }

            string runtime = Path.Combine(gamePath, "runtime");
            if (!Directory.Exists(runtime)) return SystemJava;

            try
            {
                string? found = Directory.GetFiles(runtime, "java.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(path => path.Contains("bin") && !path.Contains("javaw"));
                return found ?? SystemJava;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LauncherLog.Write($"[WARN] Java в папке игры не найдена: {error.Message}");
                return SystemJava;
            }
        }
    }
}
