using System.Collections.Generic;

namespace CustomLauncher.Core
{
    public class ServerConfig
    {
        public string Name { get; set; } = "";
        public string ServerPath { get; set; } = "";
        public string Motd { get; set; } = "BattleCraft Server";
        public int MaxPlayers { get; set; } = 20;
        public int ServerPort { get; set; } = 25565;
        public int ViewDistance { get; set; } = 10;
        public int ServerRamMb { get; set; } = 4096;
        public bool WhitelistEnabled { get; set; } = false;
        public bool EulaAccepted { get; set; } = false;
        public bool IsInstalled { get; set; } = false;
        public List<string> WhitelistedPlayers { get; set; } = new();
    }
}
