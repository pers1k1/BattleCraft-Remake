using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

            GenerateServerProperties(config);
            GenerateEula(config);

            if (config.WhitelistEnabled)
                GenerateWhitelistJson(config);

            SetState(ServerState.Starting);

            string serverDir = ResolveServerDirectory(config);
            string forgeJar = FindForgeJar(serverDir);

            int initialHeapMb = Math.Min(config.ServerRamMb, 1024);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = BuildJavaArguments(config.ServerRamMb, initialHeapMb, forgeJar),
                WorkingDirectory = serverDir,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };

            _serverProcess = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };

            _serverProcess.OutputDataReceived += OnDataReceived;
            _serverProcess.ErrorDataReceived += OnDataReceived;

            _serverProcess.Start();
            _serverInput = _serverProcess.StandardInput;
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            SetState(ServerState.Running);

            await _serverProcess.WaitForExitAsync();

            _serverInput = null;
            _serverProcess = null;
            SetState(ServerState.Stopped);
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
            string eulaPath = Path.Combine(ResolveServerDirectory(config), "eula.txt");
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

        private static string BuildJavaArguments(int maxHeapMb, int initialHeapMb, string jarFileName)
        {
            return $"-Xmx{maxHeapMb}M -Xms{initialHeapMb}M -jar \"{jarFileName}\" nogui";
        }

        private static string BoolToString(bool value)
        {
            return value ? "true" : "false";
        }

        private static string GenerateOfflineUuid(string playerName)
        {
            using var md5 = MD5.Create();
            byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName));

            // RFC 4122 Version 3 UUID
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
