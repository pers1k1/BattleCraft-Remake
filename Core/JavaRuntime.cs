using System;
using System.IO;
using System.Linq;

namespace CustomLauncher.Core
{
    public static class JavaRuntime
    {
        public const string SystemJava = "java";

        public static string BundledFolder(string gamePath) =>
            Path.Combine(gamePath, "runtime", "java-runtime-gamma", "windows-x64", "java-runtime-gamma");

        public static bool IsBundled(string javaPath) =>
            !string.Equals(javaPath, SystemJava, StringComparison.OrdinalIgnoreCase) && File.Exists(javaPath);

        public static string Find(string gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath)) return SystemJava;

            string bundled = Path.Combine(BundledFolder(gamePath), "bin", "java.exe");
            if (File.Exists(bundled)) return bundled;

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
