using System.ComponentModel;

namespace CustomLauncher.Core
{
    public sealed class GameAction
    {
        public GameAction(string group, string title, params string[] optionKeys)
        {
            Group = group;
            Title = title;
            OptionKeys = optionKeys;
        }

        public string Group { get; }
        public string Title { get; }
        public string[] OptionKeys { get; }
    }

    public sealed class GameBinding : INotifyPropertyChanged
    {
        private string _value = MinecraftKeys.Unbound;
        private bool _conflicted;
        private bool _listening;

        public GameBinding(GameAction action) => Action = action;

        public GameAction Action { get; }

        public string Title => Lang.T(Action.Title);

        public string Group => Lang.T(Action.Group);

        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                Notify(nameof(Value));
                Notify(nameof(Display));
            }
        }

        public bool Conflicted
        {
            get => _conflicted;
            set { _conflicted = value; Notify(nameof(Conflicted)); Notify(nameof(Display)); }
        }

        public bool Listening
        {
            get => _listening;
            set { _listening = value; Notify(nameof(Listening)); Notify(nameof(Display)); }
        }

        public string Display => Listening
            ? "..."
            : Conflicted ? $"[ {MinecraftKeys.Describe(Value)} ]" : MinecraftKeys.Describe(Value);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Notify(string property) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    public static class GameControls
    {
        private const string Parkour = "ПАРКУР";
        private const string Weapons = "ОРУЖИЕ";
        private const string Vehicles = "ТЕХНИКА";
        private const string Battle = "BATTLECRAFT";
        private const string Communication = "СВЯЗЬ";
        private const string Misc = "ПРОЧЕЕ";

        public static readonly GameAction[] Actions =
        {
            new(Parkour, "Лечь", "key_key.parcool.Crawl", "key_key.tacz.crawl.desc"),
            new(Parkour, "Перекат", "key_key.parcool.Dodge"),
            new(Parkour, "Скольжение по стене", "key_key.parcool.WallSlide"),
            new(Parkour, "Свисание с уступа", "key_key.parcool.HangDown", "key_key.parcool.ClingToCliff"),
            new(Parkour, "Лазание и прыжки", "key_key.parcool.WallJump", "key_key.parcool.HorizontalWallRun",
                "key_key.parcool.RideZipline", "key_key.parcool.Vault", "key_key.parcool.Breakfall"),
            new(Parkour, "Быстрый бег", "key_key.parcool.FastRun"),

            new(Weapons, "Прицеливание", "key_key.tacz.aim.desc", "key_key.superbwarfare.hold_zoom"),
            new(Weapons, "Перезарядка", "key_key.tacz.reload.desc", "key_key.superbwarfare.reload"),
            new(Weapons, "Режим огня", "key_key.tacz.fire_select.desc", "key_key.superbwarfare.fire_mode"),
            new(Weapons, "Осмотр оружия", "key_key.tacz.inspect.desc"),
            new(Weapons, "Модификация оружия", "key_key.tacz.refit.desc"),
            new(Weapons, "Ближний бой", "key_key.tacz.melee.desc", "key_key.superbwarfare.melee"),

            new(Vehicles, "Взаимодействие с техникой", "key_key.superbwarfare.interact"),
            new(Vehicles, "Выйти из техники", "key_key.superbwarfare.dismount"),
            new(Vehicles, "Наведение", "key_key.superbwarfare.vehicle_seek"),
            new(Vehicles, "Тепловизор", "key_key.superbwarfare.active_thermal_imaging"),

            new(Battle, "Действия BattleCraft", "key_key.capturepoints.capture", "key_key.knockdown.revive"),
            new(Battle, "Сдаться", "key_key.knockdown.surrender"),
            new(Battle, "Карта", "key_key.minimap.open_map"),

            new(Communication, "Голосовой чат", "key_key.push_to_talk"),
            new(Communication, "Метка на местности", "key_key.pingwheel.ping_location"),
            new(Communication, "Рация", "key_key.walkietalkie.activate"),

            new(Misc, "Приближение", "key_justzoom.keybinds.keybind.zoom"),
            new(Misc, "Прибор ночного видения", "key_key.nvg.toggle_nvg")
        };
    }
}
