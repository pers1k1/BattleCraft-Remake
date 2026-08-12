using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CustomLauncher.Core
{
    public static class GameDefaults
    {
        public static readonly Dictionary<string, string> RecommendedGraphics = new()
        {
            ["fullscreen"] = "false",
            ["guiScale"] = "0",
            ["renderDistance"] = "8",
            ["simulationDistance"] = "6",
            ["graphicsMode"] = "1",
            ["particles"] = "1",
            ["maxFps"] = "120",
            ["enableVsync"] = "true",
            ["renderClouds"] = "\"false\"",
            ["mipmapLevels"] = "4",
            ["biomeBlendRadius"] = "2",
            ["entityShadows"] = "true",
            ["ao"] = "true",
            ["gamma"] = "0.5",
            ["fov"] = "0.55",
            ["bobView"] = "true",
            ["autoJump"] = "false"
        };

        public static readonly Dictionary<string, string> RecommendedSound = new()
        {
            ["soundCategory_master"] = "1.0",
            ["soundCategory_music"] = "0.3",
            ["soundCategory_ambient"] = "0.6",
            ["soundCategory_weather"] = "0.6"
        };

        public static readonly Dictionary<string, string> RecommendedControls = new()
        {
            ["key_key.parcool.Crawl"] = "key.keyboard.z",
            ["key_key.tacz.crawl.desc"] = "key.keyboard.z",
            ["key_key.parcool.Dodge"] = "key.keyboard.x",
            ["key_justzoom.keybinds.keybind.zoom"] = "key.keyboard.c",
            ["key_key.pingwheel.ping_location"] = "key.mouse.4",
            ["key_key.push_to_talk"] = "key.mouse.5",
            ["key_key.capturepoints.capture"] = "key.keyboard.left.alt",
            ["key_key.knockdown.revive"] = "key.keyboard.left.alt",
            ["key_key.parcool.ClingToCliff"] = "key.mouse.right",
            ["key_key.parcool.HangDown"] = "key.mouse.right",
            ["key_key.parcool.WallSlide"] = "key.keyboard.x",
            ["key_key.parcool.RideZipline"] = "key.keyboard.space",
            ["key_key.parcool.HorizontalWallRun"] = "key.keyboard.space",
            ["key_key.parcool.WallJump"] = "key.keyboard.space",
            ["key_key.parcool.Vault"] = "key.keyboard.space",
            ["key_key.parcool.Breakfall"] = "key.keyboard.space",
            ["key_key.parcool.HideInBlock"] = "key.keyboard.unknown",
            ["key_key.tacz.refit.desc"] = "key.keyboard.i",
            ["key_key.superbwarfare.interact"] = "key.keyboard.f",
            ["key_key.swapOffhand"] = "key.keyboard.unknown",
            ["key_key.superbwarfare.dismount"] = "key.keyboard.l",
            ["key_key.superbwarfare.vehicle_seek"] = "key.keyboard.u",
            ["key_key.superbwarfare.free_camera"] = "key.keyboard.unknown",
            ["key_key.survival_instinct.exo_suit_dash"] = "key.keyboard.unknown"
        };

        public static void EnsureDefaults(string gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
                return;

            string optionsPath = Path.Combine(gamePath, "options.txt");
            if (File.Exists(optionsPath))
                return;

            Apply(gamePath, RecommendedGraphics.Concat(RecommendedSound).Concat(RecommendedControls));
        }

        public static void ApplyGraphics(string gamePath) =>
            Apply(gamePath, RecommendedGraphics.Concat(RecommendedSound));

        public static void ApplyControls(string gamePath) => Apply(gamePath, RecommendedControls);

        public static void Write(string gamePath, IEnumerable<KeyValuePair<string, string>> values) =>
            Apply(gamePath, values);

        public static Dictionary<string, string> Read(string gamePath)
        {
            var options = new Dictionary<string, string>();
            string optionsPath = Path.Combine(gamePath, "options.txt");

            if (!File.Exists(optionsPath))
                return options;

            foreach (string line in File.ReadAllLines(optionsPath))
            {
                int separator = line.IndexOf(':');
                if (separator > 0)
                    options[line[..separator]] = line[(separator + 1)..];
            }

            return options;
        }

        private static void Apply(string gamePath, IEnumerable<KeyValuePair<string, string>> values)
        {
            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
                return;

            string optionsPath = Path.Combine(gamePath, "options.txt");
            var merged = Read(gamePath);

            foreach (var pair in values)
                merged[pair.Key] = pair.Value;

            File.WriteAllLines(optionsPath, merged.Select(pair => $"{pair.Key}:{pair.Value}"));
        }
    }
}
