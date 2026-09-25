# BattleCraft Launcher

A custom Minecraft launcher and server manager for the BattleCraft modpack, built on .NET 8 and WPF. It handles the full client lifecycle — installing Minecraft, Forge, Java, and mods — and provides an integrated tool for provisioning and operating dedicated Forge servers.

## Overview

| Component | Version |
| --- | --- |
| Minecraft | 1.20.1 |
| Forge | 47.4.22 |
| Launcher | 19.09.26 |
| Runtime | .NET 8 (WPF, Windows 10/11) |

## Versioning

Releases are dated, not numbered. A version is the release date in `dd.MM.yy`
form - `03.08.26` is the build published on 3 August 2026. When a day needs a
second release, the date carries a revision suffix: `03.08.26hotfix` for a fix
shipped the same day, or `03.08.26v2`, `03.08.26v3` for further rebuilds
(`hotfix` and `v2` rank the same, so use one or the other per day). A hotfix
that needs a fix of its own continues as `03.08.26hotfixv2`, `03.08.26hotfixv3`
- those rank above `hotfix` and below `v3`.

The remote config files store the same version in sortable `yyyy.MM.dd` form
(`2026.08.03`, `2026.08.03hotfix`), which is what the launcher compares; the
interface always shows the `dd.MM.yy` form. Versions from before this scheme
(`8.6.6` and earlier) are still understood and always rank below a dated one.

## Client Features

- One-click installation and launch of Minecraft and Forge.
- A missing or unreadable Forge profile installs itself. The launch path no longer trusts the presence of the `versions` folder: the profile manifest has to parse and list libraries, and when it does not - a half-finished install, a deleted file, an aborted first run - the launcher wipes the stale Forge profiles of the same Minecraft version, downloads the installer and runs it instead of stopping at a "Forge not found" dialog. The same repair runs again if the version list still comes back without the profile right before launch, and only a second failure reaches the player as an error.
- Automatic Java detection and provisioning (Adoptium Temurin 17) when no suitable runtime is present.
- Self-updating launcher and modpack with resilient downloads: automatic retries with exponential backoff, a stall guard that fails hung connections fast, and HTTP range resume that continues interrupted files instead of restarting them.
- Microsoft authentication without WebView2, plus offline accounts.
- Offline nicknames follow the Minecraft rule - Latin letters, digits and `_`, 3 to 16 characters. The nickname fields refuse anything else while it is typed, pasted or dropped in, and a nickname edited into `launcher_config.json` by hand is caught on start and before every launch: the launcher returns to the login screen and asks for a valid one instead of starting the game under a name the server will not accept. Server whitelist entries are held to the same rule.
- Customizable interface: 41 color theme presets (Sakura by default), sixteen of them a beige and muted-neutral family built around dusty rose, taupe, cocoa, clay, terracotta, caramel, latte, sand, marzipan, cashmere, powder and ivory, each paired with a primary mixed towards its own accent plus manual HEX colors, custom icon, neon bloom, adjustable terminal transparency, and a glass-style UI that lets the scene show through the panels.
- Custom presets: name the colors you tuned by hand and they join the preset list, saved with the rest of the configuration and removable from the same row.
- Theme handoff to the game: the chosen primary and accent colors are written to `launcher_theme/theme.json` in the game folder whenever they change and again before every launch, so the BattleCraft mod dresses its own interface in the palette picked here instead of a fixed one.
- Bilingual interface (Russian and English): the language is chosen during first-run setup and can be switched at any time in the settings.
- Every animation runs on a purpose-built engine instead of WPF storyboards: a single render tick drives all motion from closed-form easing math, click particles are drawn as one visual layer, and the launcher detects the current refresh rate of the display it sits on — re-checking when the window moves to another monitor or the display mode changes — so motion is paced to the monitor and never renders frames it cannot show.
- Tactile, animated UI: buttons burst into particles on click, the sidebar reacts with glow and motion, and tabs, settings, and login transitions are fully animated. Theme presets cross-fade smoothly instead of switching instantly, dropdowns slide open with sprung easing and their items glide on hover, slider thumbs grow and emit a pulsing halo while dragged, text fields swell on focus and gently bounce with every keystroke.
- Cohesive squircle design language: dropdown menus, tooltips, and context menus are rounded, soft-shadowed, and themed to the active colors — no stock-gray Windows chrome leaks anywhere; combo arrows flip over with a spring, checkboxes spring on hover.
- In-app ChangeLogs viewer with separate launcher and modpack/server-map tabs, fetched live from the remote config in the interface language (Russian or English).
- Living pixel-art background: a hand-rendered seasonal scene with a day/night cycle, parallax mountains and a forest of swaying trees, and dynamic weather — rain with thunderstorms, snow that drifts and piles up, fog, and gusts that tear autumn leaves and spring cherry blossom off the tree crowns they grew on — never out of an empty sky. Petals and leaves settle on the ground where they land and fade out after a while instead of vanishing on contact, petals carry the colour of the crown they fell from, and cherry blossom covers the ground under the trees whenever sakura stands in the scene. Every particle — falling petals, rain, snow, gusts, fog, comets, the kite and the person with the umbrella — is lit by the same daylight value as the rest of the scene, so a thunderstorm darkens them together with the sky instead of leaving them glowing at full brightness. The scene is simulated and rendered on a dedicated background thread, so the UI stays responsive even while it animates; animation pauses while the window is minimized or in the background to keep idle resource usage low.
- Selectable background — four options to suit any taste, all tinted live by the active theme colors: the animated pixel scene; "Theme flow", an animated soft-gradient backdrop whose color fields slowly drift, rotate and blend into each other; "Night aurora", a static vector night sky with accent-tinted aurora ribbons, a twinkling starfield, a moon and occasional shooting stars over mountain silhouettes; and "Minimal", a completely still theme-matched gradient with a subtle accent glow for those who prefer no motion at all. Switching backgrounds cross-fades smoothly, and the heavy pixel renderer is put to sleep whenever a non-scene background is active.
- In-launcher game settings (`~/game`): graphics (view and simulation distance, framerate cap labelled with the detected refresh rate, GUI scale, quality, particles, field of view, brightness, windowed mode, vsync, clouds, entity shadows, view bobbing, auto jump), sound levels, and a rebindable list of the modpack's own actions grouped per mod — parkour, weapons, vehicles, BattleCraft, comms. Vanilla options are covered too: mouse sensitivity and inversion, pause on lost focus, toggle sneak and sprint, smooth lighting, and the core game keys — movement, jump, sneak, sprint, attack, use, inventory, chat, player list. Click a binding and press any key or mouse button to capture it; a duplicate turns red and gets bracketed, the way vanilla marks a clash. Written straight into `options.txt`, with one button to restore the recommended layout. The window refuses to open while the game is running, since Minecraft rewrites `options.txt` on exit and would undo everything set from here.
- System check on every start. `Core/SystemRequirements` inspects Windows build and architecture, the Visual C++ 2015-2022 runtime, the WebView2 component behind the Microsoft sign-in, WPF hardware acceleration (with the video card and driver version read from the registry), memory against the heap given to the game, write access to the launcher and game folders, free space on the game drive and in the temp folder, the shape of the game path, the bundled Java, and the installed mods. Apart from the game path nothing blocks the launcher: it warns. The panel opens by itself only when something is off and from the sidebar at any time; Visual C++ and WebView2 install from their buttons, the driver button opens the vendor's download page, missing Java and missing mods route into the existing install flows. Without hardware acceleration the glow is switched off for the session so the window stops flickering.
- The game path is checked before it is used. `Core/GamePathRules` accepts only a local `X:\` path whose folders hold Latin letters, digits, spaces and `_ - . ( )`, with no reserved Windows names (`CON`, `LPT1`, a trailing dot) and no more than 90 characters. Non-Latin letters, `! # % ; + & '` and the like (the first five are syntax in Java jar paths and the classpath), network and relative paths are refused with the reason and the exact folder or character named. The setup wizard and the settings refuse such a folder and offer a free one at the root of the same drive (`C:\BattleCraft`, then `C:\BattleCraft2\BattleCraft` and so on), probing that it can actually be created. An existing install on a bad path stops at the Play button and in the system check with a "Move" action: on the same drive the folder is renamed in place and server paths follow it, on another drive the pack is installed afresh. Choosing a non-empty folder no longer wipes it silently in the wizard, and a folder that holds the launcher itself or its settings is never wiped.
- The settings folder has a fallback. `Documents\CustomLauncher` is tried first; when it is redirected to a cloud folder, held by an antivirus folder shield or otherwise unwritable, the launcher moves its config, log and crash reports to `%LOCALAPPDATA%\CustomLauncher` (and to a folder next to the exe as a last resort), carrying an existing config over. `AppSettings.Save` returns whether it wrote and logs what went wrong instead of swallowing it, an unreadable config is kept as `.broken` next to the new one, and the system check reports which folder is actually in use. This was the root cause behind a player whose settings, logins and installed-pack state reset on every start.
- A download torn mid-file is repaired, not endured. Forge opens every jar through `SecureJar`, so a single truncated library kills the launch in a second (`UnionFileSystemProvider`, `ZipException`) before any game log exists. `Core/BrokenJars` walks `libraries`, `versions` and `mods`, opens each jar as a zip and lists the ones that no longer read. The walk is parallel because it is bound by the disk, not the CPU: 181 archives on a cold folder take 1020 ms one by one and 222 ms in parallel. When the captured output shows that signature, the launcher deletes exactly those files and reinstalls: the modpack when a mod is damaged, Forge and the vanilla files otherwise, then starts the game again. The system check reports damaged files too. Measured, not assumed: CmlLib re-downloads a truncated *vanilla* library once checksums are on, but never the libraries the Forge installer put there - hence the sweep plus a real reinstall.
- The game's own output is captured. `stdout` and `stderr` of the Minecraft process go to `game-output.log` next to the launcher log, with the last 200 lines kept in memory, and the debug console setting now streams them into the launcher terminal instead of opening a separate window. This is the only evidence when the game dies before it writes `logs/latest.log` - the case where a player saw "exit code 1" and an empty log folder. If the JVM itself refused to start (`Unrecognized VM option`, `Could not create the Java Virtual Machine`, `Could not reserve enough space for object heap`), the launcher says so, drops the pack's GC flags (`AppSettings.SafeJvm`) or halves the heap, and starts the game again by itself.
- Downloading the game survives a hostile network. CmlLib fetches the ~800 MB of assets in twelve parallel streams, which an antivirus doing HTTPS inspection tears mid-stream: the read threw `Received an unexpected EOF or 0 bytes from the transport stream` and the whole install died, no matter that the retry handler sits one layer below the response body. `KeepDownloading` now wraps `InstallAsync` and `CreateProcessAsync`: on a network error it resumes (everything already downloaded stays on disk) for up to twelve attempts and narrows the download lanes 12 → 4 → 2 → 1 through a fresh `ParallelGameInstaller`. The lane count that finally worked is saved in `AppSettings.DownloadLanes`, so the next install starts calm instead of hammering the same wall.
- A broken connection explains itself. `Core/NetworkTrouble` recognises a torn HTTPS request anywhere in the exception chain (`Received an unexpected EOF or 0 bytes from the transport stream` and its relatives) and the error dialog then names the usual causes - HTTPS scanning in an antivirus, a VPN or proxy, provider-side blocking, unstable Wi-Fi - instead of printing the raw .NET sentence. Version checks and the managed-config manifest log what actually failed rather than swallowing it.
- Silent failures are gone. The game's exit code and duration are checked: a non-zero code, or an exit before the sound engine started, opens a window with the last real errors from the game log (mixin noise filtered out), the newest Minecraft crash report and the path to `logs/latest.log`. Unpacking the pack verifies that `mods` is not empty, the BattleCraft jar verifies its size, a missing bundled Java reinstalls itself before launch, and the swallowed failures that mattered - silent Microsoft sign-in, taking and restoring personal options, wiping the old pack folders, deleting the previous BattleCraft jar - now reach both the log and the terminal panel.
- Managed mod settings: a manifest in the remote config lists the config keys that must be identical for every player. Before launch the launcher rewrites only those keys and leaves personal settings untouched. Missing files are created from bundled templates with their real TOML sections, so first-launch flags work before the mod creates its own config. Existing files are never replaced by a template, writes are atomic, and paths stay inside the game folder.
- Recommended client defaults are written as soon as the launcher enters the main screen or accepts a new game folder, without requiring the game-settings page to be opened first. They are applied only to an installation that has no `options.txt` yet: a player who already tuned the game keeps what they set, and takes a newer set with the "Recommended" button when they want it. A fresh install gets fullscreen, GUI scale 2, field of view 70, entity render distance at maximum, music at 2%, and Minecraft accessibility onboarding disabled. Key bindings put the microphone mute on `+`, night vision goggles on `N` with their mode switch on `J`, the map on `M`, the fire selector of both weapon mods on `H`, weapon inspection on `G`, the BattleCraft points key on `I`, push-to-talk on `V`, melee of both weapon mods on mouse button 5, scope zoom and decoy flares on `-`, and leave the bindings that used to share those keys unbound. The radio transmit key of the BattleCraft mod takes Caps Lock, the points key moves to the grave-accent key left of the digits, capture and revive keep left Alt, goggle zoom sits on Y, interacting with a gun in hand joins melee on mouse button 5, and the config-screen keys of the weapon and parkour mods are unbound, since those settings are shipped by the pack and the mod refuses to open their screens in game. The framerate cap follows the refresh rate of the monitor containing the launcher, rounded up to whole tens, and is synchronized again before each game launch.
- Every `options.txt` the launcher writes starts with the `version` line naming the data version of the game it is built for. Minecraft treats a file without it as ancient and runs it through the whole data-fixer chain, which tries to read the modern string key bindings as LWJGL2 numbers, throws, and drops the file whole. The first launch on a fresh install therefore came up fully vanilla, and the settings only stuck after the game had saved its own file and the "Recommended" button was pressed again.
- The recommended set also writes `chatLineSpacing`. The pack's own mod widens the chat line spacing on startup and saves `options.txt` right away - before Forge re-reads the file for the mod key bindings - so that save used to push every modded binding back to its default and the first launch of a fresh install came up with the mod defaults. With the value already in place the mod leaves the file alone.
- Field of view and mouse sensitivity are written the way Minecraft stores them: the game keeps the field of view as an offset from 70 degrees divided by 40, not as degrees, and reads the sensitivity with three decimals. The launcher converts both, so the number on the slider is the number the game shows.
- The framerate cap is rounded up to whole tens instead of matching the refresh rate exactly: the Embeddium slider moves in steps of five and pulls a neighbouring value in, and a cap sitting exactly on the refresh rate stutters under vsync. A 144 Hz display gets 150, a 101 Hz one gets 110.
- Writing `options.txt` never takes the launcher down. The file is replaced through a temporary file and an atomic move, and a locked file - a second copy of the launcher started by a self-update, or the game itself - is logged and skipped instead of raising an unhandled error. A settings file that cannot be read is left alone rather than overwritten with the recommended set, so a lock never costs the player his own settings.
- The Minecraft language follows the launcher language: Russian selects `ru_ru`, English selects `en_us`. An existing `options.txt` is synchronized immediately when the launcher language changes and checked again before every game launch.
- A modpack update never touches personal settings: `options.txt` (and the OptiFine/shader variants) is taken aside before the archive is unpacked and put back afterwards, so a pack that happens to carry someone else's options file cannot replace the player's own, and cannot mask the recommended defaults either.
- The loader's own splash is started dark rather than red. Forge reads `FML_EARLY_WINDOW_DARK` from the game process environment before it looks at `options.txt`, so the launcher sets it on every launch and the mod-loading window matches the pack instead of flashing red first.
- Free disk space is checked before the game folder is chosen and before the modpack or a server is installed, so an install cannot die halfway through a full disk.
- The client collector is chosen by heap size. From 8 GB up the launcher switches to Shenandoah, whose pauses stay in single-digit milliseconds and no longer land on Distant Horizons building far terrain; below that it keeps G1, because on a small heap G1 pauses are short anyway and Shenandoah's read barriers would only cost throughput. Switching to Shenandoah also strips the G1 flags CmlLib puts on the command line by default, since a JVM given two collectors at once refuses to start. The server always keeps G1, where pause length does not affect what the player sees.
- Discord Rich Presence integration. Connection, every presence line the launcher sends and every failure are written to the log, so what Discord received can be read without opening Discord.
- Unified install/launch log with rolling crash reports retained in the launcher's configuration directory; the detected OS (e.g. Windows 11) is reported on the boot screen and in the terminal.
- Every step of a session is tagged in the log: `[SYS]` for the launcher itself, `[SETUP]` for the first-run wizard, `[AUTH]` for sign-in, `[UPD]` for version checks and self-update, `[PACK]` for the modpack and the BattleCraft mod, `[LOADER]` for Forge and Java, `[PLAY]` for launching the game and everything that follows its exit, `[SERVER]` for the server tab, `[NET]` for downloads, `[UI]` for what was changed in the interface, plus `[WARN]` and `[ERR]`. Reading a player's log now shows the order of events instead of a wall of identical lines: the names of the files being downloaded stay in the terminal panel and no longer reach the file, while folder changes, chosen colors, background, language, key sets, server commands and state changes do.
- The interface hides on demand: the eye button in the title bar fades the panels out and leaves the animated scene alone on screen, and a second press brings them back.
- Forge library installation notice with installer output captured to the log.
- Automatic cleanup of stale Distant Horizons server data on launch.

## Server Features

- Create and manage multiple Forge servers from a single interface. Server folders are named with Latin letters, digits, `_` and `-` only, and two servers never share a folder.
- Staged installation that preserves progress and resumes after a dropped connection.
- GUI configuration of `server.properties` (MOTD, port, view distance, RAM). Managed keys are merged into the existing file, so manual edits to any other key survive a restart.
- Whitelist management with offline UUID generation.
- Built-in console with command input.
- World restore from a local backup.
- Automatic updates for server mods and the world map, tracked per server so updating one server never hides updates for another. Each stage records what it finished, so a BattleCraft mod that failed to download is fetched again on its own instead of pulling the whole server mod archive a second time, and an install that lost only that jar keeps the server it already provisioned.

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
