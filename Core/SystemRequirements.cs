using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CustomLauncher.Core
{
    public enum RequirementState { Ok, Warning, Missing }

    public enum RequirementFix { None, InstallVcRedist, InstallWebView2, OpenUrl, InstallJava, ReinstallModpack, OpenGameFolder }

    public sealed class RequirementCheck
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required string Detail { get; init; }
        public required RequirementState State { get; init; }
        public RequirementFix Fix { get; init; } = RequirementFix.None;
        public string FixTarget { get; init; } = "";
        public string FixLabel { get; init; } = "";
    }

    public sealed class RequirementContext
    {
        public required string GamePath { get; init; }
        public required bool ModpackInstalled { get; init; }
        public required int RenderTier { get; init; }
        public required int RamMb { get; init; }
        public required bool LicensedAccount { get; init; }
    }

    public static class SystemRequirements
    {
        public const long MinimalWindowsBuild = 14393;
        public const long TempSpaceBytes = 2L * 1024 * 1024 * 1024;
        public const long MinimalMemoryBytes = 6L * 1024 * 1024 * 1024;
        public const int LongPathLimit = 90;

        public static List<RequirementCheck> Inspect(RequirementContext context)
        {
            var checks = new List<RequirementCheck>
            {
                CheckWindows(),
                CheckVcRedist(),
                CheckWebView2(context.LicensedAccount),
                CheckRendering(context.RenderTier),
                CheckMemory(context.RamMb),
                CheckConfigFolder()
            };

            if (!string.IsNullOrWhiteSpace(context.GamePath))
            {
                checks.Add(CheckGameFolder(context.GamePath));
                checks.Add(CheckGameSpace(context.GamePath));
                checks.Add(CheckGamePathShape(context.GamePath));
            }

            checks.Add(CheckTempSpace());

            if (context.ModpackInstalled && !string.IsNullOrWhiteSpace(context.GamePath))
            {
                checks.Add(CheckJava(context.GamePath));
                checks.Add(CheckModpackFiles(context.GamePath));
            }

            return checks;
        }

        public static bool AllGood(IEnumerable<RequirementCheck> checks) =>
            checks.All(check => check.State == RequirementState.Ok);

        private static RequirementCheck CheckWindows()
        {
            Version version = Environment.OSVersion.Version;
            string title = Lang.T("Windows и разрядность");

            if (!Environment.Is64BitOperatingSystem)
            {
                return Missing(title, "windows",
                    Lang.T("Нужна 64-битная Windows: сборка и Java собраны только под x64."));
            }

            if (version.Build < MinimalWindowsBuild)
            {
                return Missing(title, "windows",
                    Lang.F("Нужна Windows 10 сборки {0} или новее, сейчас {1}. На старых сборках лаунчер работает с ошибками.",
                        MinimalWindowsBuild, version.Build));
            }

            return Ok(title, "windows", Lang.F("Windows {0}, 64 бита", version.Build));
        }

        private static RequirementCheck CheckVcRedist()
        {
            string title = Lang.T("Visual C++ 2015-2022 x64");

            if (VcRedist.Installed())
            {
                string version = VcRedist.InstalledVersion();
                return Ok(title, "vcredist",
                    string.IsNullOrEmpty(version) ? Lang.T("Установлен") : Lang.F("Установлен, версия {0}", version));
            }

            return new RequirementCheck
            {
                Id = "vcredist",
                Title = title,
                Detail = Lang.T("Без этой библиотеки Microsoft лаунчер запускается через раз и падает без сообщения. Ставится автоматически."),
                State = RequirementState.Missing,
                Fix = RequirementFix.InstallVcRedist,
                FixLabel = Lang.T("Установить")
            };
        }

        private static RequirementCheck CheckWebView2(bool licensedAccount)
        {
            string title = Lang.T("Вход через Microsoft (WebView2)");
            string version = WebView2Runtime.Version();

            if (!string.IsNullOrEmpty(version))
                return Ok(title, "webview2", Lang.F("Установлен, версия {0}", version));

            return new RequirementCheck
            {
                Id = "webview2",
                Title = title,
                Detail = Lang.T("Компонент Microsoft Edge WebView2 не установлен: окно входа по лицензии не откроется. Ставится автоматически."),
                State = licensedAccount ? RequirementState.Missing : RequirementState.Warning,
                Fix = RequirementFix.InstallWebView2,
                FixLabel = Lang.T("Установить")
            };
        }

        private static RequirementCheck CheckRendering(int renderTier)
        {
            string title = Lang.T("Аппаратное ускорение");
            GpuDriver driver = GpuDriver.Read();
            string card = driver.Describe();

            if (renderTier >= 2)
            {
                return Ok(title, "render", string.IsNullOrEmpty(card) ? Lang.T("Полное ускорение") : card);
            }

            string detail = renderTier <= 0
                ? Lang.T("Windows рисует окно процессором: будут чёрные прямоугольники, мерцание и следы. Причина почти всегда в видеодрайвере.")
                : Lang.T("Ускорение работает частично, возможны мерцание и следы. Обычно лечится свежим видеодрайвером.");

            if (!string.IsNullOrEmpty(card)) detail += "\n" + Lang.F("Видеокарта: {0}", card);

            return new RequirementCheck
            {
                Id = "render",
                Title = title,
                Detail = detail,
                State = RequirementState.Warning,
                Fix = RequirementFix.OpenUrl,
                FixTarget = driver.DownloadUrl,
                FixLabel = Lang.T("Драйвер")
            };
        }

        private static RequirementCheck CheckMemory(int ramMb)
        {
            string title = Lang.T("Оперативная память");

            if (!SystemMemory.TryRead(out long total, out _))
            {
                return Warning(title, "memory", Lang.T("Объём памяти определить не удалось."));
            }

            string amount = DiskSpace.Describe(total);

            if (total < MinimalMemoryBytes)
            {
                return Warning(title, "memory",
                    Lang.F("Всего {0}. Сборке нужно от 8 ГБ, иначе игра вылетает на загрузке мира.", amount));
            }

            long reserved = ramMb * 1024L * 1024L;
            if (reserved > total - 2L * 1024 * 1024 * 1024)
            {
                return Warning(title, "memory",
                    Lang.F("Игре выделено {0} из {1}. Оставьте системе хотя бы 2 ГБ, иначе Windows начнёт выгружать игру в файл подкачки.",
                        DiskSpace.Describe(reserved), amount));
            }

            return Ok(title, "memory", Lang.F("{0}, игре выделено {1}", amount, DiskSpace.Describe(reserved)));
        }

        private static RequirementCheck CheckConfigFolder()
        {
            string title = Lang.T("Папка настроек лаунчера");
            string path = AppSettings.GetConfigDir();

            return Writable(path, out string error)
                ? Ok(title, "config", path)
                : Missing(title, "config",
                    Lang.F("Не получается писать в {0}: {1}\nНастройки и логи не сохранятся.", path, error));
        }

        private static RequirementCheck CheckGameFolder(string gamePath)
        {
            string title = Lang.T("Папка игры");

            if (!Directory.Exists(gamePath))
            {
                return Missing(title, "game-folder", Lang.F("Папки {0} нет на месте.", gamePath));
            }

            return Writable(gamePath, out string error)
                ? Ok(title, "game-folder", gamePath)
                : new RequirementCheck
                {
                    Id = "game-folder",
                    Title = title,
                    Detail = Lang.F("Не получается писать в {0}: {1}\nМоды и настройки не установятся. Перенесите сборку из системной папки.", gamePath, error),
                    State = RequirementState.Missing,
                    Fix = RequirementFix.OpenGameFolder,
                    FixLabel = Lang.T("Открыть")
                };
        }

        private static RequirementCheck CheckGameSpace(string gamePath)
        {
            string title = Lang.T("Место для сборки");

            if (!DiskSpace.TryGetFreeBytes(gamePath, out long free))
            {
                return Warning(title, "game-space", Lang.T("Свободное место определить не удалось."));
            }

            string free_ = DiskSpace.Describe(free);

            if (free < DiskSpace.ClientRequiredBytes)
            {
                return Warning(title, "game-space",
                    Lang.F("Свободно {0}, а на установку и обновления нужно {1}. Скачивание оборвётся на середине.",
                        free_, DiskSpace.Describe(DiskSpace.ClientRequiredBytes)));
            }

            return Ok(title, "game-space", Lang.F("Свободно {0}", free_));
        }

        private static RequirementCheck CheckGamePathShape(string gamePath)
        {
            string title = Lang.T("Путь к сборке");
            var complaints = new List<string>();

            if (gamePath.Any(symbol => symbol > 127))
                complaints.Add(Lang.T("в пути есть буквы не латиницей - часть модов и Java спотыкаются об это"));

            if (gamePath.Length > LongPathLimit)
                complaints.Add(Lang.F("путь длиннее {0} символов - распаковка модов обрывается на длинных именах", LongPathLimit));

            if (complaints.Count == 0) return Ok(title, "game-path", gamePath);

            return Warning(title, "game-path",
                gamePath + "\n" + Lang.T("Лучше перенести сборку, например в C:\\BattleCraft.") + "\n" + string.Join("; ", complaints));
        }

        private static RequirementCheck CheckTempSpace()
        {
            string title = Lang.T("Временная папка");
            string temp = Path.GetTempPath();

            if (!Writable(temp, out string error))
            {
                return Missing(title, "temp",
                    Lang.F("Не получается писать во временную папку {0}: {1}\nЛаунчер не сможет ни обновиться, ни скачать сборку.", temp, error));
            }

            if (DiskSpace.TryGetFreeBytes(temp, out long free) && free < TempSpaceBytes)
            {
                return Warning(title, "temp",
                    Lang.F("Во временной папке свободно {0}. Архив сборки качается именно туда, нужно от {1}.",
                        DiskSpace.Describe(free), DiskSpace.Describe(TempSpaceBytes)));
            }

            return Ok(title, "temp", temp);
        }

        private static RequirementCheck CheckJava(string gamePath)
        {
            string title = Lang.T("Java для игры");
            string java = JavaRuntime.Find(gamePath);

            if (JavaRuntime.IsBundled(java))
                return Ok(title, "java", java);

            return new RequirementCheck
            {
                Id = "java",
                Title = title,
                Detail = Lang.T("Java сборки не найдена в папке игры. Без неё запуск либо падает, либо берёт чужую Java и вылетает без сообщения."),
                State = RequirementState.Missing,
                Fix = RequirementFix.InstallJava,
                FixLabel = Lang.T("Установить")
            };
        }

        private static RequirementCheck CheckModpackFiles(string gamePath)
        {
            string title = Lang.T("Файлы сборки");
            string mods = Path.Combine(gamePath, "mods");

            string[] jars;
            try
            {
                jars = Directory.Exists(mods) ? Directory.GetFiles(mods, "*.jar") : Array.Empty<string>();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return Warning(title, "modpack", Lang.F("Папка модов не читается: {0}", error.Message));
            }

            if (jars.Length == 0)
            {
                return Reinstall(title, Lang.T("В папке mods нет ни одного мода. Сборка не установилась или её вычистил антивирус."));
            }

            if (!jars.Any(jar => Path.GetFileName(jar).StartsWith("battlecraft", StringComparison.OrdinalIgnoreCase)))
            {
                return Reinstall(title, Lang.T("Мод BattleCraft отсутствует, на сервер зайти не выйдет."));
            }

            string[] empty = jars.Where(jar => new FileInfo(jar).Length == 0).ToArray();
            if (empty.Length > 0)
            {
                return Reinstall(title,
                    Lang.F("Пустых файлов модов: {0}. Скачивание оборвалось, нужна переустановка.", empty.Length));
            }

            return Ok(title, "modpack", Lang.F("Модов установлено: {0}", jars.Length));
        }

        private static RequirementCheck Reinstall(string title, string detail) => new()
        {
            Id = "modpack",
            Title = title,
            Detail = detail,
            State = RequirementState.Missing,
            Fix = RequirementFix.ReinstallModpack,
            FixLabel = Lang.T("Перекачать")
        };

        private static bool Writable(string path, out string error)
        {
            error = "";
            string probe = Path.Combine(path, "bcr_write_test.tmp");

            try
            {
                Directory.CreateDirectory(path);
                File.WriteAllText(probe, "bcr");
                File.Delete(probe);
                return true;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
            {
                error = failure.Message;
                return false;
            }
        }

        private static RequirementCheck Ok(string title, string id, string detail) =>
            new() { Id = id, Title = title, Detail = detail, State = RequirementState.Ok };

        private static RequirementCheck Warning(string title, string id, string detail) =>
            new() { Id = id, Title = title, Detail = detail, State = RequirementState.Warning };

        private static RequirementCheck Missing(string title, string id, string detail) =>
            new() { Id = id, Title = title, Detail = detail, State = RequirementState.Missing };
    }
}
