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

            CleanStaleSessionLocks(serverDir);

            GenerateEula(config);
            GenerateUserJvmArgs(config, serverDir);
            GenerateServerProperties(config);

            if (config.WhitelistEnabled)
                GenerateWhitelistJson(config);

            LaunchServerProcess(javaPath, serverDir);
        }

        public async Task StopAsync()
        {
            var process = _serverProcess;
            if (CurrentState != ServerState.Running || process == null)
                return;

            SetState(ServerState.Stopping);
            SendCommand("stop");

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            try
            {
                var exitTask = process.WaitForExitAsync();
                if (await Task.WhenAny(exitTask, timeoutTask) == timeoutTask)
                    ForceKillProcess();
            }
            catch { }
        }

        public async Task RestartAsync(ServerConfig config, string javaPath)
        {
            await StopAsync();
            await Task.Delay(2000);
            await StartAsync(config, javaPath);
        }

        public void ForceKillProcess()
        {
            var process = _serverProcess;
            try { process?.Kill(entireProcessTree: true); } catch { }
            _serverInput = null;
            _serverProcess = null;
            SetState(ServerState.Stopped);
        }

        public void SendCommand(string command)
        {
            var input = _serverInput;
            if (input == null || CurrentState != ServerState.Running)
                return;

            try
            {
                input.WriteLine(command);
                input.Flush();
            }
            catch { }
        }

        public static string SanitizePathSegment(string name)
        {
            string sanitized = Regex.Replace(name.Trim(), @"[^\w\-.]", "_");
            return string.IsNullOrWhiteSpace(sanitized) ? "server" : sanitized;
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
            var process = _serverProcess;
            if (process == null) return;

            try
            {
                await process.WaitForExitAsync();
            }
            catch { }
            finally
            {
                if (_serverProcess == process)
                {
                    _serverInput = null;
                    _serverProcess = null;
                    SetState(ServerState.Stopped);
                }
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

        private static void CleanStaleSessionLocks(string serverDir)
        {
            if (!Directory.Exists(serverDir))
                return;

            foreach (string lockFile in Directory.GetFiles(serverDir, "session.lock", SearchOption.AllDirectories))
            {
                try { File.Delete(lockFile); } catch { }
            }
        }

        private static void GenerateUserJvmArgs(ServerConfig config, string serverDir)
        {
            string userJvmArgsPath = Path.Combine(serverDir, "user_jvm_args.txt");
            int minHeapMb = Math.Min(config.ServerRamMb, 1024);
            File.WriteAllText(userJvmArgsPath, $"-Xmx{config.ServerRamMb}M\n-Xms{minHeapMb}M\n-Dfml.queryResult=confirm\n");
        }

        public static void GenerateServerProperties(ServerConfig config)
        {
            string serverDir = ResolveServerDirectory(config);
            EnsureDirectoryExists(serverDir);

            string propertiesPath = Path.Combine(serverDir, "server.properties");

            var propertyLines = new List<string>
            {
                "level-name=sigma",
                $"motd={config.Motd}",
                $"max-players={config.MaxPlayers}",
                $"server-port={config.ServerPort}",
                $"view-distance={config.ViewDistance}",
                $"simulation-distance={config.ViewDistance}",
                "gamemode=survival",
                "difficulty=normal",
                "pvp=true",
                $"online-mode={BoolToString(config.OnlineMode)}",
                "allow-nether=false",
                "allow-flight=true",
                "spawn-protection=0",
                $"spawn-animals={BoolToString(config.SpawnAnimals)}",
                $"spawn-monsters={BoolToString(config.SpawnMonsters)}",
                "spawn-npcs=true",
                $"white-list={BoolToString(config.WhitelistEnabled)}",
                $"enforce-whitelist={BoolToString(config.WhitelistEnabled)}"
            };

            if (!string.IsNullOrWhiteSpace(config.ServerIp))
            {
                propertyLines.Add($"server-ip={config.ServerIp}");
            }

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
