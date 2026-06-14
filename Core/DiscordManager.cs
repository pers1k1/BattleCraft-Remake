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

        private const string ClientId = "1510061496590401688";
        private const ulong OwnerDiscordId = 650390226643976213;

        public string LauncherVersion { get; set; } = "";
        public string ModpackVersion { get; set; } = "";

        public void Initialize()
        {
            if (_isInitialized) return;

            _client = new DiscordRpcClient(ClientId);
            _client.Logger = new ConsoleLogger { Level = LogLevel.Warning };

            _client.OnReady += (sender, e) =>
            {
                if (e.User.ID == OwnerDiscordId)
                {
                    _isOwner = true;
                    if (_currentState == "menu") SetMenuState();
                }
            };

            _client.Initialize();

            _isInitialized = true;
            SetMenuState();
        }

        public void SetMenuState()
        {
            _currentState = "menu";

            if (_client == null)
            {
                Initialize();
                return;
            }
            if (!_client.IsInitialized) return;

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

            if (_client != null)
            {
                try { _client.ClearPresence(); } catch { }
                _client.Dispose();
                _client = null;
            }
            _isInitialized = false;
        }

        public void Dispose()
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
            _isInitialized = false;
        }
    }
}
