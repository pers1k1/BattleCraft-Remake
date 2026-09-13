using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CustomLauncher.Core
{
    // WHY: когда игра падает раньше, чем создаст свой logs/latest.log, единственная улика
    // WHY: это её stdout и stderr - без перехвата игрок видел «ошибка 1» и пустую папку логов
    public sealed class GameOutput
    {
        public const string FileName = "game-output.log";
        private const int KeptLines = 200;

        private readonly object _lock = new();
        private readonly Queue<string> _tail = new();
        private readonly string _path;
        private StreamWriter? _writer;

        public GameOutput()
        {
            _path = Path.Combine(AppSettings.GetConfigDir(), FileName);
        }

        public string Path_ => _path;

        public void Begin(string command)
        {
            lock (_lock)
            {
                _tail.Clear();
                Close();

                try
                {
                    _writer = new StreamWriter(_path, false, new UTF8Encoding(false)) { AutoFlush = true };
                    _writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {command}");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    LauncherLog.Write($"[WARN] Вывод игры не пишется в файл: {error.Message}");
                    _writer = null;
                }
            }
        }

        public void Add(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;

            lock (_lock)
            {
                _tail.Enqueue(line);
                while (_tail.Count > KeptLines) _tail.Dequeue();

                try { _writer?.WriteLine(line); }
                catch (IOException) { _writer = null; }
            }
        }

        public List<string> Tail(int lines)
        {
            lock (_lock)
            {
                return _tail.Reverse().Take(lines).Reverse().ToList();
            }
        }

        public bool Mentions(params string[] markers)
        {
            lock (_lock)
            {
                return _tail.Any(line => markers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)));
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                try { _writer?.Dispose(); }
                catch (IOException) { }
                _writer = null;
            }
        }
    }
}
