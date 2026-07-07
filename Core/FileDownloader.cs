using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CustomLauncher.Core
{
    public class FileDownloader
    {
        private static readonly HttpClient _client = new HttpClient
        {
            Timeout = TimeSpan.FromHours(1)
        };

        static FileDownloader()
        {
            _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }

        public event Action<double>? ProgressChanged;
        public event Action<string>? SpeedChanged;
        public event Action<string>? StatusChanged;
        public event Action<string>? LogMessage;

        public async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
        {
            string fileName = SafeName(url);

            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogMessage?.Invoke(Lang.F("Ошибка скачивания {0} (HTTP {1})", fileName, (int)response.StatusCode));
                throw new Exception(Lang.F("Ошибка скачивания (HTTP {0})", response.StatusCode));
            }

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;
            LogMessage?.Invoke(totalBytes > 0
                ? Lang.F("Загрузка: {0} ({1})", fileName, FormatSize(totalBytes))
                : Lang.F("Загрузка: {0}", fileName));

            byte[] buffer = new byte[81920];

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, true);

            long totalRead = 0;
            int bytesRead;
            long bytesReadSinceLastUpdate = 0;
            var stopwatch = Stopwatch.StartNew();
            var total = Stopwatch.StartNew();
            var lastUpdate = DateTime.UtcNow;
            var lastLog = DateTime.UtcNow;
            double lastSpeed = 0;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);

                totalRead += bytesRead;
                bytesReadSinceLastUpdate += bytesRead;

                var nowUtc = DateTime.UtcNow;
                if ((nowUtc - lastUpdate).TotalMilliseconds > 200)
                {
                    double elapsed = stopwatch.Elapsed.TotalSeconds;
                    lastSpeed = elapsed > 0 ? bytesReadSinceLastUpdate / elapsed : 0;

                    if (totalBytes > 0)
                        ProgressChanged?.Invoke((double)totalRead / totalBytes * 100);

                    SpeedChanged?.Invoke(FormatSpeed(lastSpeed));
                    StatusChanged?.Invoke($"{FormatSize(totalRead)} / {FormatSize(totalBytes)}");

                    stopwatch.Restart();
                    bytesReadSinceLastUpdate = 0;
                    lastUpdate = nowUtc;
                }

                if ((nowUtc - lastLog).TotalMilliseconds > 1200)
                {
                    if (totalBytes > 0)
                    {
                        double percent = (double)totalRead / totalBytes * 100;
                        LogMessage?.Invoke($"{fileName}: {percent:F0}% · {FormatSize(totalRead)} / {FormatSize(totalBytes)} · {FormatSpeed(lastSpeed)}");
                    }
                    else
                    {
                        LogMessage?.Invoke($"{fileName}: {FormatSize(totalRead)} · {FormatSpeed(lastSpeed)}");
                    }
                    lastLog = nowUtc;
                }
            }

            ProgressChanged?.Invoke(100);

            double avg = total.Elapsed.TotalSeconds > 0 ? totalRead / total.Elapsed.TotalSeconds : 0;
            LogMessage?.Invoke(Lang.F("Готово: {0} — {1} за {2} ({3})", fileName, FormatSize(totalRead), FormatDuration(total.Elapsed), FormatSpeed(avg)));
        }

        private static string SafeName(string url)
        {
            try
            {
                string name = Path.GetFileName(new Uri(url).AbsolutePath);
                return string.IsNullOrEmpty(name) ? Lang.T("файл") : name;
            }
            catch { return Lang.T("файл"); }
        }

        public static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond > 1024 * 1024) return $"{bytesPerSecond / 1024 / 1024:F1} MB/s";
            if (bytesPerSecond > 1024) return $"{bytesPerSecond / 1024:F0} KB/s";
            return $"{bytesPerSecond:F0} B/s";
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "?";
            if (bytes > 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
            if (bytes > 1024) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        private static string FormatDuration(TimeSpan t)
        {
            if (t.TotalMinutes >= 1) return Lang.F("{0} мин {1} с", (int)t.TotalMinutes, t.Seconds);
            return Lang.F("{0:F0} с", t.TotalSeconds);
        }
    }
}
