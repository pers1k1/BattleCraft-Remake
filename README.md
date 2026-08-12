# BattleCraft Launcher

A custom Minecraft launcher and server manager for the BattleCraft modpack, built on .NET 8 and WPF. It handles the full client lifecycle — installing Minecraft, Forge, Java, and mods — and provides an integrated tool for provisioning and operating dedicated Forge servers.

## Overview

| Component | Version |
| --- | --- |
| Minecraft | 1.20.1 |
| Forge | 47.4.22 |
| Launcher | 12.08.26hotfix |
| Runtime | .NET 8 (WPF, Windows 10/11) |

## Versioning

Releases are dated, not numbered. A version is the release date in `dd.MM.yy`
form - `03.08.26` is the build published on 3 August 2026. When a day needs a
second release, the date carries a revision suffix: `03.08.26hotfix` for a fix
shipped the same day, or `03.08.26v2`, `03.08.26v3` for further rebuilds
(`hotfix` and `v2` rank the same, so use one or the other per day).

The remote config files store the same version in sortable `yyyy.MM.dd` form
(`2026.08.03`, `2026.08.03hotfix`), which is what the launcher compares; the
interface always shows the `dd.MM.yy` form. Versions from before this scheme
(`8.6.6` and earlier) are still understood and always rank below a dated one.

## Client Features

- One-click installation and launch of Minecraft and Forge.
- Automatic Java detection and provisioning (Adoptium Temurin 17) when no suitable runtime is present.
- Self-updating launcher and modpack with resilient downloads: automatic retries with exponential backoff, a stall guard that fails hung connections fast, and HTTP range resume that continues interrupted files instead of restarting them.
- Microsoft authentication without WebView2, plus offline accounts.
- Customizable interface: 18 color theme presets (Sakura by default) plus manual HEX colors, custom icon, neon bloom, adjustable terminal transparency, and a glass-style UI that lets the scene show through the panels.
- Bilingual interface (Russian and English): the language is chosen during first-run setup and can be switched at any time in the settings.
- Every animation runs on a purpose-built engine instead of WPF storyboards: a single render tick drives all motion from closed-form easing math, click particles are drawn as one visual layer, and the launcher detects the current refresh rate of the display it sits on — re-checking when the window moves to another monitor or the display mode changes — so motion is paced to the monitor and never renders frames it cannot show.
- Tactile, animated UI: buttons burst into particles on click, the sidebar reacts with glow and motion, and tabs, settings, and login transitions are fully animated. Theme presets cross-fade smoothly instead of switching instantly, dropdowns slide open with sprung easing and their items glide on hover, slider thumbs grow and emit a pulsing halo while dragged, text fields swell on focus and gently bounce with every keystroke.
- Cohesive squircle design language: dropdown menus, tooltips, and context menus are rounded, soft-shadowed, and themed to the active colors — no stock-gray Windows chrome leaks anywhere; combo arrows flip over with a spring, checkboxes spring on hover.
- In-app ChangeLogs viewer with separate launcher and modpack/server-map tabs, fetched live from the remote config in the interface language (Russian or English).
- Living pixel-art background: a hand-rendered seasonal scene with a day/night cycle, parallax mountains and a forest of swaying trees, and dynamic weather — rain with thunderstorms, snow that drifts and piles up, fog, falling autumn leaves and spring cherry-blossom petals. The scene is simulated and rendered on a dedicated background thread, so the UI stays responsive even while it animates; animation pauses while the window is minimized or in the background to keep idle resource usage low.
- Selectable background — four options to suit any taste, all tinted live by the active theme colors: the animated pixel scene; "Theme flow", an animated soft-gradient backdrop whose color fields slowly drift, rotate and blend into each other; "Night aurora", a static vector night sky with accent-tinted aurora ribbons, a twinkling starfield, a moon and occasional shooting stars over mountain silhouettes; and "Minimal", a completely still theme-matched gradient with a subtle accent glow for those who prefer no motion at all. Switching backgrounds cross-fades smoothly, and the heavy pixel renderer is put to sleep whenever a non-scene background is active.
- Discord Rich Presence integration.
- Unified install/launch log with rolling crash reports retained in the launcher's configuration directory; the detected OS (e.g. Windows 11) is reported on the boot screen and in the terminal.
- Forge library installation notice with installer output captured to the log.
- Automatic cleanup of stale Distant Horizons server data on launch.

## Server Features

- Create and manage multiple Forge servers from a single interface.
- Staged installation that preserves progress and resumes after a dropped connection.
- GUI configuration of `server.properties` (MOTD, port, view distance, RAM). Managed keys are merged into the existing file, so manual edits to any other key survive a restart.
- Whitelist management with offline UUID generation.
- Built-in console with command input.
- World restore from a local backup.
- Automatic updates for server mods and the world map, tracked per server so updating one server never hides updates for another.

## Recommended Server Settings

Defaults tuned for running the server and the client on the same machine (6 cores, 16 GB RAM, two players). They are applied by `ServerConfig` and written by `ServerManager` on every start.

| Setting | Value | Reason |
| --- | --- | --- |
| `view-distance` | 8 | Distant Horizons already covers the far view; 12 only inflates the chunk working set. |
| `simulation-distance` | 6 | Entity and block ticking is the most expensive part of a tick and needs a far smaller radius than rendering. |
| `sync-chunk-writes` | false | Synchronous chunk writes stall the main thread on every save. |
| `max-tick-time` | 60000 | Prevents the watchdog from killing the server during a long chunk load. |
| Server heap | 4096 MB | `-Xms` equals `-Xmx` so the heap never resizes mid-game. |
| GC | G1 with tuned pause and region flags | Default G1 on a small heap produces multi-second pauses under chunk load. |

Distant Horizons must have `enableDistantGeneration` and `enableServerGeneration` set to `false` on both sides. With generation on, DH runs its own world generator threads on the server and the client at once, which saturates every core and writes generated chunks back into the region files.

The modpack ships Canary, Saturn, spark, ModernFix, FerriteCore and Memory Leak Fix. Canary and Radium are both Lithium ports and must never be installed together.

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
- Newtonsoft.Json

## License and attribution

Released under the [GNU Affero General Public License v3.0](LICENSE) with the
attribution terms in [NOTICE](NOTICE).

In practice this means any fork, redistribution, or hosted service built on this
code must publish its complete source under the same license and must keep a
visible credit to the author - pers1k1, https://github.com/pers1k1. Closed-source
derivatives and builds with the attribution stripped out are not permitted.
