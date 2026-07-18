using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CustomLauncher.Core
{
    public class FileDownloader
    {
        private const int MaxAttempts = 5;

        public event Action<double>? ProgressChanged;
        public event Action<string>? SpeedChanged;
        public event Action<string>? StatusChanged;
        public event Action<string>? LogMessage;

        public async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
        {
            string fileName = SafeName(url);
            Exception? lastError = null;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    await DownloadCoreAsync(url, destinationPath, fileName, resume: attempt > 1, cancellationToken);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
                {
                    lastError = ex;
                    if (attempt == MaxAttempts) break;
                    LogMessage?.Invoke(Lang.F("Обрыв загрузки {0} — повтор {1}/{2}", fileName, attempt + 1, MaxAttempts));
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(3000, 400 * attempt)), cancellationToken);
                }
            }

            LogMessage?.Invoke(Lang.F("Ошибка скачивания {0}: {1}", fileName, lastError?.Message ?? ""));
            throw new Exception(Lang.F("Не удалось скачать {0} за {1} попыток.", fileName, MaxAttempts), lastError);
        }

        private async Task DownloadCoreAsync(string url, string destinationPath, string fileName, bool resume, CancellationToken cancellationToken)
        {
            long existing = 0;
            if (resume)
            {
                try { var fi = new FileInfo(destinationPath); if (fi.Exists) existing = fi.Length; }
                catch { existing = 0; }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            if (existing > 0)
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

            using var response = await ResilientHttpClientFactory.Shared.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            bool resumed = response.StatusCode == HttpStatusCode.PartialContent && existing > 0;
            if (!resumed && existing > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (response.Content.Headers.ContentRange?.Length == existing)
                {
                    LogMessage?.Invoke(Lang.F("Уже скачан: {0}", fileName));
                    ProgressChanged?.Invoke(100);
                    return;
                }
                try { File.Delete(destinationPath); } catch { }
            }
            if (!resumed && !response.IsSuccessStatusCode)
            {
                LogMessage?.Invoke(Lang.F("Ошибка скачивания {0} (HTTP {1})", fileName, (int)response.StatusCode));
                throw new HttpRequestException(Lang.F("Ошибка скачивания (HTTP {0})", response.StatusCode));
            }
            if (!resumed) existing = 0;

            long contentBytes = response.Content.Headers.ContentLength ?? -1L;
            long totalBytes = contentBytes > 0 ? existing + contentBytes : -1L;
            LogMessage?.Invoke(resumed
                ? Lang.F("Докачка: {0} с {1}", fileName, FormatSize(existing))
                : totalBytes > 0
                    ? Lang.F("Загрузка: {0} ({1})", fileName, FormatSize(totalBytes))
                    : Lang.F("Загрузка: {0}", fileName));

            byte[] buffer = new byte[81920];

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(destinationPath,
                resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 81920, true);

            long totalRead = existing;
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

            if (totalBytes > 0 && totalRead < totalBytes)
                throw new IOException(Lang.F("Загрузка оборвалась: получено {0} из {1}.", FormatSize(totalRead), FormatSize(totalBytes)));

            ProgressChanged?.Invoke(100);

            double avg = total.Elapsed.TotalSeconds > 0 ? (totalRead - existing) / total.Elapsed.TotalSeconds : 0;
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
