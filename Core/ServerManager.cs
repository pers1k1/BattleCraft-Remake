using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CustomLauncher.Core
{
    public enum ServerState
    {
        Stopped,
        Starting,
        Running,
        Stopping
    }

    public class ServerManager
    {
        private Process? _serverProcess;
        private StreamWriter? _serverInput;

        public ServerState CurrentState { get; private set; } = ServerState.Stopped;

        public event Action<string>? OutputReceived;
        public event Action<ServerState>? StateChanged;

        public async Task StartAsync(ServerConfig config, string javaPath)
        {
            if (CurrentState != ServerState.Stopped)
                return;

            string serverDir = ResolveServerDirectory(config);
            EnsureDirectoryExists(serverDir);

            GenerateEula(config);
            GenerateUserJvmArgs(config, serverDir);

            bool isFirstRun = !File.Exists(Path.Combine(serverDir, "server.properties"));

            if (isFirstRun)
                await PerformFirstRunSetup(config, javaPath, serverDir);

            GenerateServerProperties(config);

            if (config.WhitelistEnabled)
                GenerateWhitelistJson(config);

            LaunchServerProcess(javaPath, serverDir);
        }

        public async Task StopAsync()
        {
            if (CurrentState != ServerState.Running || _serverProcess == null)
                return;

            SetState(ServerState.Stopping);
            SendCommand("stop");

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var exitTask = _serverProcess.WaitForExitAsync();

            if (await Task.WhenAny(exitTask, timeoutTask) == timeoutTask)
            {
                try { _serverProcess.Kill(); } catch { }
            }
        }

        public async Task RestartAsync(ServerConfig config, string javaPath)
        {
            await StopAsync();
            await Task.Delay(2000);
            await StartAsync(config, javaPath);
        }

        public void SendCommand(string command)
        {
            if (_serverInput == null || CurrentState != ServerState.Running)
                return;

            try
            {
                _serverInput.WriteLine(command);
                _serverInput.Flush();
            }
            catch { }
        }

        public static string SanitizePathSegment(string name)
        {
            string sanitized = Regex.Replace(name.Trim(), @"[^\w\-.]", "_");
            return string.IsNullOrWhiteSpace(sanitized) ? "server" : sanitized;
        }

        private async Task PerformFirstRunSetup(ServerConfig config, string javaPath, string serverDir)
        {
            OutputReceived?.Invoke("[SYS] Первый запуск: генерация файлов...");
            SetState(ServerState.Starting);

            var serverReadySignal = new TaskCompletionSource<bool>();

            Process firstRunProcess = CreateServerProcess(javaPath, serverDir);
            firstRunProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                OutputReceived?.Invoke(e.Data);
                if (e.Data.Contains("Done") && e.Data.Contains("For help"))
                    serverReadySignal.TrySetResult(true);
            };
            firstRunProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) OutputReceived?.Invoke(e.Data);
            };

            firstRunProcess.Start();
            var firstRunInput = firstRunProcess.StandardInput;
            firstRunProcess.BeginOutputReadLine();
            firstRunProcess.BeginErrorReadLine();

            SetState(ServerState.Running);

            var readyTimeout = Task.Delay(TimeSpan.FromMinutes(5));
            var readyResult = await Task.WhenAny(serverReadySignal.Task, readyTimeout);

            if (readyResult == readyTimeout)
                OutputReceived?.Invoke("[WARN] Таймаут первого запуска, принудительная остановка...");

            OutputReceived?.Invoke("[SYS] Остановка для конфигурации...");
            try { firstRunInput.WriteLine("stop"); firstRunInput.Flush(); } catch { }

            var exitTimeout = Task.Delay(TimeSpan.FromSeconds(30));
            var exitResult = await Task.WhenAny(firstRunProcess.WaitForExitAsync(), exitTimeout);

            if (exitResult == exitTimeout)
            {
                try { firstRunProcess.Kill(); } catch { }
            }

            SetState(ServerState.Stopped);

            OutputReceived?.Invoke("[SYS] Применение настроек...");
            await Task.Delay(1500);
        }

        private void LaunchServerProcess(string javaPath, string serverDir)
        {
            SetState(ServerState.Starting);

            _serverProcess = CreateServerProcess(javaPath, serverDir);
            _serverProcess.OutputDataReceived += OnDataReceived;
            _serverProcess.ErrorDataReceived += OnDataReceived;

            _serverProcess.Start();
            _serverInput = _serverProcess.StandardInput;
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            SetState(ServerState.Running);

            _ = MonitorProcessExitAsync();
        }

        private async Task MonitorProcessExitAsync()
        {
            if (_serverProcess == null) return;

            try
            {
                await _serverProcess.WaitForExitAsync();
            }
            catch { }
            finally
            {
                _serverInput = null;
                _serverProcess = null;
                SetState(ServerState.Stopped);
            }
        }

        private Process CreateServerProcess(string javaPath, string serverDir)
        {
            string arguments = BuildLaunchArguments(serverDir);

            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = arguments,
                    WorkingDirectory = serverDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                },
                EnableRaisingEvents = true
            };
        }

        private static string BuildLaunchArguments(string serverDir)
        {
            string? winArgsFile = FindArgsFile(serverDir, "win_args.txt");
            if (winArgsFile != null)
            {
                string relativePath = Path.GetRelativePath(serverDir, winArgsFile).Replace('\\', '/');
                return $"@user_jvm_args.txt @{relativePath} nogui";
            }

            string? unixArgsFile = FindArgsFile(serverDir, "unix_args.txt");
            if (unixArgsFile != null)
            {
                string relativePath = Path.GetRelativePath(serverDir, unixArgsFile).Replace('\\', '/');
                return $"@user_jvm_args.txt @{relativePath} nogui";
            }

            string forgeJar = FindForgeJar(serverDir);
            return $"@user_jvm_args.txt -jar \"{forgeJar}\" nogui";
        }

        private static string? FindArgsFile(string serverDir, string fileName)
        {
            if (!Directory.Exists(serverDir))
                return null;

            string[] found = Directory.GetFiles(serverDir, fileName, SearchOption.AllDirectories);
            return found.Length > 0 ? found[0] : null;
        }

        private static string FindForgeJar(string serverDirectory)
        {
            if (!Directory.Exists(serverDirectory))
                return "server.jar";

            string[] forgeJars = Directory.GetFiles(serverDirectory, "forge-*.jar");
            if (forgeJars.Length > 0)
                return Path.GetFileName(forgeJars[0]);

            string[] anyJars = Directory.GetFiles(serverDirectory, "*.jar");
            if (anyJars.Length > 0)
                return Path.GetFileName(anyJars[0]);

            return "server.jar";
        }

        private static void GenerateUserJvmArgs(ServerConfig config, string serverDir)
        {
            string userJvmArgsPath = Path.Combine(serverDir, "user_jvm_args.txt");
            int minHeapMb = Math.Min(config.ServerRamMb, 1024);
            File.WriteAllText(userJvmArgsPath, $"-Xmx{config.ServerRamMb}M\n-Xms{minHeapMb}M\n");
        }

        public static void GenerateServerProperties(ServerConfig config)
        {
            string serverDir = ResolveServerDirectory(config);
            EnsureDirectoryExists(serverDir);

            string propertiesPath = Path.Combine(serverDir, "server.properties");

            string[] propertyLines =
            {
                $"motd={config.Motd}",
                $"max-players={config.MaxPlayers}",
                $"server-port={config.ServerPort}",
                $"view-distance={config.ViewDistance}",
                $"simulation-distance={config.ViewDistance}",
                "gamemode=survival",
                "difficulty=normal",
                "pvp=true",
                "online-mode=false",
                "allow-nether=false",
                "allow-flight=true",
                "spawn-protection=0",
                $"white-list={BoolToString(config.WhitelistEnabled)}",
                $"enforce-whitelist={BoolToString(config.WhitelistEnabled)}"
            };

            File.WriteAllLines(propertiesPath, propertyLines);
        }

        public static void GenerateEula(ServerConfig config)
        {
            string serverDir = ResolveServerDirectory(config);
            EnsureDirectoryExists(serverDir);
            string eulaPath = Path.Combine(serverDir, "eula.txt");
            File.WriteAllText(eulaPath, $"eula={BoolToString(config.EulaAccepted)}");
        }

        public static void GenerateWhitelistJson(ServerConfig config)
        {
            string whitelistPath = Path.Combine(ResolveServerDirectory(config), "whitelist.json");

            var whitelistEntries = config.WhitelistedPlayers
                .Select(playerName => new
                {
                    uuid = GenerateOfflineUuid(playerName),
                    name = playerName
                })
                .ToArray();

            string serializedJson = JsonConvert.SerializeObject(whitelistEntries, Formatting.Indented);
            File.WriteAllText(whitelistPath, serializedJson);
        }

        private static string ResolveServerDirectory(ServerConfig config)
        {
            return Path.Combine(config.ServerPath, "server");
        }

        private static void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);
        }

        private static string BoolToString(bool value)
        {
            return value ? "true" : "false";
        }

        private static string GenerateOfflineUuid(string playerName)
        {
            using var md5 = MD5.Create();
            byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName));

            hashBytes[6] = (byte)((hashBytes[6] & 0x0F) | 0x30);
            hashBytes[8] = (byte)((hashBytes[8] & 0x3F) | 0x80);

            var segments = new StringBuilder(36);
            for (int i = 0; i < 16; i++)
            {
                segments.Append(hashBytes[i].ToString("x2"));
                if (i == 3 || i == 5 || i == 7 || i == 9)
                    segments.Append('-');
            }

            return segments.ToString();
        }

        private void OnDataReceived(object sender, DataReceivedEventArgs eventArgs)
        {
            if (eventArgs.Data != null)
                OutputReceived?.Invoke(eventArgs.Data);
        }

        private void SetState(ServerState newState)
        {
            CurrentState = newState;
            StateChanged?.Invoke(newState);
        }
    }
}
