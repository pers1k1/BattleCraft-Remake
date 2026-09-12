namespace CustomLauncher.Core
{
    public static class GameVersions
    {
        public const string Minecraft = "1.20.1";
        // WHY: без строки version игра гонит options.txt через все датафиксеры
        // WHY: с нуля, спотыкается на строковой клавише и теряет весь файл целиком
        public const string MinecraftDataVersion = "3465";
        public const string Forge = "47.4.22";
        public const string ForgeProfileId = Minecraft + "-forge-" + Forge;
        public const string Display = Minecraft + " · Forge " + Forge;
        public const string ForgeInstallerUrl =
            "https://maven.minecraftforge.net/net/minecraftforge/forge/"
            + Minecraft + "-" + Forge + "/forge-" + Minecraft + "-" + Forge + "-installer.jar";
    }
}
