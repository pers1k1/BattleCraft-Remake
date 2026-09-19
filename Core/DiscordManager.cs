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

            _client.OnConnectionFailed += (sender, e) => LauncherLog.Write($"[DISCORD] Подключение не удалось: труба {e.FailedPipe}");

            _client.Initialize();
            LauncherLog.Write("[DISCORD] Статус подключается");

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
                Details = Lang.F("В главном меню | v{0} (Моды: v{1})", LauncherVersion, ModpackVersion),
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
            LauncherLog.Write($"[DISCORD] Статус: {presence.Details} / {presence.State}");
        }

        public void ReleaseForGame()
        {
            _currentState = "playing";

            if (_client != null)
            {
                try { _client.ClearPresence(); }
                catch (Exception error) { LauncherLog.Write($"[DISCORD] Очистка статуса не прошла: {error.Message}"); }
                _client.Dispose();
                _client = null;
            }
            _isInitialized = false;
            LauncherLog.Write("[DISCORD] Статус отдан игре");
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
