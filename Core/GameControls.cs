using System.ComponentModel;

namespace CustomLauncher.Core
{
    public sealed class GameAction
    {
        public GameAction(string title, params string[] optionKeys)
        {
            Title = title;
            OptionKeys = optionKeys;
        }

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
            set { _conflicted = value; Notify(nameof(Conflicted)); }
        }

        public bool Listening
        {
            get => _listening;
            set { _listening = value; Notify(nameof(Listening)); Notify(nameof(Display)); }
        }

        public string Display => Listening ? "..." : MinecraftKeys.Describe(Value);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Notify(string property) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    public static class GameControls
    {
        public static readonly GameAction[] Actions =
        {
            new("Лечь", "key_key.parcool.Crawl", "key_key.tacz.crawl.desc"),
            new("Перекат", "key_key.parcool.Dodge"),
            new("Паркур: лазание", "key_key.parcool.ClingToCliff", "key_key.parcool.HangDown",
                "key_key.parcool.WallSlide", "key_key.parcool.RideZipline",
                "key_key.parcool.HorizontalWallRun", "key_key.parcool.WallJump",
                "key_key.parcool.Vault", "key_key.parcool.Breakfall"),
            new("Приближение", "key_justzoom.keybinds.keybind.zoom"),
            new("Метка на местности", "key_key.pingwheel.ping_location"),
            new("Голосовой чат", "key_key.push_to_talk"),
            new("Действия BattleCraft", "key_key.capturepoints.capture", "key_key.knockdown.revive"),
            new("Взаимодействие с техникой", "key_key.superbwarfare.interact"),
            new("Выйти из техники", "key_key.superbwarfare.dismount"),
            new("Прицеливание", "key_key.tacz.aim.desc", "key_key.superbwarfare.hold_zoom"),
            new("Перезарядка", "key_key.tacz.reload.desc", "key_key.superbwarfare.reload"),
            new("Режим огня", "key_key.tacz.fire_select.desc", "key_key.superbwarfare.fire_mode"),
            new("Осмотр оружия", "key_key.tacz.inspect.desc"),
            new("Карта", "key_key.minimap.open_map"),
            new("Рация", "key_key.walkietalkie.activate"),
            new("Прибор ночного видения", "key_key.nvg.toggle_nvg")
        };
    }
}
