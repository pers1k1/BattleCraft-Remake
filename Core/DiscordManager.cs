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
                Details = "В главном меню",
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

            var presence = new RichPresence()
            {
                Details = "Играет в BattleCraft",
                State = _isOwner ? $"Owner | Сервер: {serverName}" : $"User | Сервер: {serverName}",
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
