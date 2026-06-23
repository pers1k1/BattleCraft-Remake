# BattleCraft Launcher

A custom Minecraft launcher and server manager for the BattleCraft modpack, built on .NET 8 and WPF. It handles the full client lifecycle — installing Minecraft, Forge, Java, and mods — and provides an integrated tool for provisioning and operating dedicated Forge servers.

## Overview

| Component | Version |
| --- | --- |
| Minecraft | 1.20.1 |
| Forge | 47.4.20 |
| Launcher | 8.0.2 |
| Runtime | .NET 8 (WPF, Windows 10/11) |

## Client Features

- One-click installation and launch of Minecraft and Forge.
- Automatic Java detection and provisioning (Adoptium Temurin 17) when no suitable runtime is present.
- Self-updating launcher and modpack with resumable, retry-on-failure downloads.
- Microsoft authentication without WebView2, plus offline accounts.
- Customizable interface: accent colors (Sakura by default), custom icon, neon bloom, adjustable terminal transparency, and a glass-style UI that lets the scene show through the panels.
- Tactile, animated UI: buttons burst into particles on click, the sidebar reacts with glow and motion, and tabs, settings, and login transitions are fully animated.
- In-app ChangeLogs viewer with separate launcher and modpack/server-map tabs, fetched live from the remote config.
- Living pixel-art background: a hand-rendered seasonal scene with a day/night cycle, parallax mountains and a forest of swaying trees, and dynamic weather — rain with thunderstorms, snow that drifts and piles up, fog, falling autumn leaves and spring cherry-blossom petals. Animation pauses while the window is minimized or in the background to keep idle resource usage low.
- Discord Rich Presence integration.
- Unified install/launch log with rolling crash reports retained in the launcher's configuration directory; the detected OS (e.g. Windows 11) is reported on the boot screen and in the terminal.
- Forge library installation notice with installer output captured to the log.
- Automatic cleanup of stale Distant Horizons server data on launch.

## Server Features

- Create and manage multiple Forge servers from a single interface.
- Staged installation that preserves progress and resumes after a dropped connection.
- GUI configuration of `server.properties` (MOTD, port, view distance, RAM).
- Whitelist management with offline UUID generation.
- Built-in console with command input.
- World restore from a local backup.
- Automatic updates for server mods and the world map.

## Building

```bash
dotnet build
```

## Publishing

```bash
dotnet publish -c Release -p:PublishSingleFile=true -o publish
```

## Tech Stack

- .NET 8 / WPF
- [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) — Minecraft launch core
- DiscordRichPresence — Rich Presence integration
- Newtonsoft.Json, SharpZipLib

## License

Released under the [MIT License](LICENSE).
