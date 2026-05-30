using System;
using DiscordRPC;
using DiscordRPC.Logging;

namespace CustomLauncher.Core
{
    public class DiscordManager : IDisposable
    {
        private DiscordRpcClient? _client;
        private bool _isInitialized;
        private bool _isOwner = false;
        private string _currentState = "menu";
        private string _currentServer = "";

        private const string ClientId = "1510061496590401688"; 
        
        public string LauncherVersion { get; set; } = "";
        public string ModpackVersion { get; set; } = "";

        public void Initialize()
        {
            if (_isInitialized) return;

            _client = new DiscordRpcClient(ClientId);
            _client.Logger = new ConsoleLogger { Level = LogLevel.Warning };

            _client.OnReady += (sender, e) =>
            {
                if (e.User.ID == 650390226643976213)
                {
                    _isOwner = true;
                    if (_currentState == "menu") SetMenuState();
                    else if (_currentState == "playing") SetPlayingState(_currentServer);
                }
            };

            _client.Initialize();
            
            SetMenuState();
            _isInitialized = true;
        }

        public void SetMenuState()
        {
            _currentState = "menu";
            if (_client == null || !_client.IsInitialized) return;

            var presence = new RichPresence()
            {
                Details = $"В главном меню | v{LauncherVersion} (Моды: v{ModpackVersion})",
                State = _isOwner ? "Owner" : "User",
                Assets = new Assets()
                {
                    LargeImageKey = "rpc_icon", 
                    LargeImageText = "BattleCraft Remake"
                },
                Buttons = new Button[]
                {
                    new Button() { Label = "GitHub", Url = "https://github.com/pers1k1/BattleCraft-Remake" }
                }
            };
            _client.SetPresence(presence);
        }

        public void SetPlayingState(string serverName)
        {
            _currentState = "playing";
            _currentServer = serverName;
            if (_client == null || !_client.IsInitialized) return;

            string stateText = (serverName == "Одиночная игра" || string.IsNullOrEmpty(serverName)) ? "Одиночная игра" : $"Сервер: {serverName}";

            var presence = new RichPresence()
            {
                Details = $"Играет в BattleCraft | v{LauncherVersion} (Моды: v{ModpackVersion})",
                State = _isOwner ? $"Owner | {stateText}" : $"User | {stateText}",
                Assets = new Assets()
                {
                    LargeImageKey = "rpc_icon",
                    LargeImageText = "BattleCraft Remake"
                },
                Timestamps = Timestamps.Now,
                Buttons = new Button[]
                {
                    new Button() { Label = "GitHub", Url = "https://github.com/pers1k1/BattleCraft-Remake" }
                }
            };
            _client.SetPresence(presence);
        }

        public void Dispose()
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
        }
    }
}
