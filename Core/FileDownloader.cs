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

        public async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Ошибка скачивания (HTTP {response.StatusCode})");

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;
            byte[] buffer = new byte[81920];

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, true);

            long totalRead = 0;
            int bytesRead;
            long bytesReadSinceLastUpdate = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastUpdate = DateTime.UtcNow;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);

                totalRead += bytesRead;
                bytesReadSinceLastUpdate += bytesRead;

                if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds > 200)
                {
                    double elapsed = stopwatch.Elapsed.TotalSeconds;
                    double speedBytesPerSec = elapsed > 0 ? bytesReadSinceLastUpdate / elapsed : 0;

                    if (totalBytes > 0)
                        ProgressChanged?.Invoke((double)totalRead / totalBytes * 100);

                    SpeedChanged?.Invoke(FormatSpeed(speedBytesPerSec));
                    StatusChanged?.Invoke($"{FormatSize(totalRead)} / {FormatSize(totalBytes)}");

                    stopwatch.Restart();
                    bytesReadSinceLastUpdate = 0;
                    lastUpdate = DateTime.UtcNow;
                }
            }

            ProgressChanged?.Invoke(100);
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond > 1024 * 1024) return $"{bytesPerSecond / 1024 / 1024:F1} MB/s";
            if (bytesPerSecond > 1024) return $"{bytesPerSecond / 1024:F0} KB/s";
            return $"{bytesPerSecond:F0} B/s";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes > 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
            if (bytes > 1024) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }
    }
}
