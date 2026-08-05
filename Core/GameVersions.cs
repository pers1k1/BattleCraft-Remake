namespace CustomLauncher.Core
{
    public static class GameVersions
    {
        public const string Minecraft = "1.20.1";
        public const string Forge = "47.4.22";
        public const string ForgeProfileId = Minecraft + "-forge-" + Forge;
        public const string Display = Minecraft + " · Forge " + Forge;
        public const string ForgeInstallerUrl =
            "https://maven.minecraftforge.net/net/minecraftforge/forge/"
            + Minecraft + "-" + Forge + "/forge-" + Minecraft + "-" + Forge + "-installer.jar";
    }
}
