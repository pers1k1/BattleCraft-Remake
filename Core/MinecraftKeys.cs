using System.Collections.Generic;
using System.Windows.Input;

namespace CustomLauncher.Core
{
    public static class MinecraftKeys
    {
        public const string Unbound = "key.keyboard.unknown";

        private static readonly Dictionary<Key, string> NamedKeys = new()
        {
            [Key.Space] = "space",
            [Key.Tab] = "tab",
            [Key.Escape] = "escape",
            [Key.Enter] = "enter",
            [Key.Back] = "backspace",
            [Key.CapsLock] = "caps.lock",
            [Key.LeftShift] = "left.shift",
            [Key.RightShift] = "right.shift",
            [Key.LeftCtrl] = "left.control",
            [Key.RightCtrl] = "right.control",
            [Key.LeftAlt] = "left.alt",
            [Key.RightAlt] = "right.alt",
            [Key.Up] = "up",
            [Key.Down] = "down",
            [Key.Left] = "left",
            [Key.Right] = "right",
            [Key.Delete] = "delete",
            [Key.Insert] = "insert",
            [Key.Home] = "home",
            [Key.End] = "end",
            [Key.PageUp] = "page.up",
            [Key.PageDown] = "page.down",
            [Key.OemMinus] = "minus",
            [Key.OemPlus] = "equal",
            [Key.OemComma] = "comma",
            [Key.OemPeriod] = "period"
        };

        private static readonly Dictionary<string, string> DisplayNames = new()
        {
            ["key.keyboard.space"] = "Пробел",
            ["key.keyboard.left.shift"] = "Shift",
            ["key.keyboard.right.shift"] = "Shift (пр.)",
            ["key.keyboard.left.control"] = "Ctrl",
            ["key.keyboard.right.control"] = "Ctrl (пр.)",
            ["key.keyboard.left.alt"] = "Alt",
            ["key.keyboard.right.alt"] = "Alt (пр.)",
            ["key.keyboard.caps.lock"] = "Caps Lock",
            ["key.keyboard.page.up"] = "Page Up",
            ["key.keyboard.page.down"] = "Page Down",
            ["key.mouse.left"] = "ЛКМ",
            ["key.mouse.right"] = "ПКМ",
            ["key.mouse.middle"] = "СКМ",
            ["key.mouse.4"] = "Мышь 4",
            ["key.mouse.5"] = "Мышь 5",
            [Unbound] = "нет"
        };

        public static string? FromKey(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
                return "key.keyboard." + key.ToString().ToLowerInvariant();

            if (key >= Key.D0 && key <= Key.D9)
                return "key.keyboard." + (key - Key.D0);

            if (key >= Key.F1 && key <= Key.F12)
                return "key.keyboard." + key.ToString().ToLowerInvariant();

            return NamedKeys.TryGetValue(key, out string? named) ? "key.keyboard." + named : null;
        }

        public static string? FromMouse(MouseButton button) => button switch
        {
            MouseButton.Left => "key.mouse.left",
            MouseButton.Right => "key.mouse.right",
            MouseButton.Middle => "key.mouse.middle",
            MouseButton.XButton1 => "key.mouse.4",
            MouseButton.XButton2 => "key.mouse.5",
            _ => null
        };

        public static string Describe(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DisplayNames[Unbound];

            if (DisplayNames.TryGetValue(value, out string? display))
                return display;

            int lastDot = value.LastIndexOf('.');
            string tail = lastDot >= 0 ? value[(lastDot + 1)..] : value;

            return tail.ToUpperInvariant();
        }
    }
}
