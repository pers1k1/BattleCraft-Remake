using System;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;

namespace CustomLauncher.Core
{
    public static class Nickname
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;
        public const string RuleMessage = "Никнейм: от 3 до 16 символов, только латиница, цифры и _";
        public const string RejectedMessage = "Никнейм из настроек не подходит: только латиница, цифры и _ от 3 до 16 символов. Введите новый.";

        public static bool Allows(char symbol) => char.IsAsciiLetterOrDigit(symbol) || symbol == '_';

        public static bool IsValid(string? nick)
        {
            if (string.IsNullOrEmpty(nick)) return false;
            if (nick.Length < MinLength || nick.Length > MaxLength) return false;

            foreach (char symbol in nick)
                if (!Allows(symbol)) return false;

            return true;
        }

        public static string Filter(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            var kept = new StringBuilder(raw.Length);
            foreach (char symbol in raw)
                if (Allows(symbol) && kept.Length < MaxLength) kept.Append(symbol);

            return kept.ToString();
        }

        public static void Restrict(TextBox box)
        {
            box.MaxLength = MaxLength;
            box.PreviewTextInput += RejectForeignInput;
            box.PreviewKeyDown += RejectSpace;
            box.TextChanged += StripForeignText;
        }

        private static void RejectForeignInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char symbol in e.Text)
                if (!Allows(symbol)) { e.Handled = true; return; }
        }

        private static void RejectSpace(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) e.Handled = true;
        }

        private static void StripForeignText(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox box) return;

            string kept = Filter(box.Text);
            if (kept == box.Text) return;

            int caret = box.CaretIndex - (box.Text.Length - kept.Length);
            box.Text = kept;
            box.CaretIndex = Math.Clamp(caret, 0, kept.Length);
        }
    }
}
