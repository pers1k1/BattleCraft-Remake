using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CustomLauncher.Core
{
    public static class ResilientHttpClientFactory
    {
        private static readonly Lazy<HttpClient> _shared = new(CreateClient);

        public static HttpClient Shared => _shared.Value;

        public static event Action<string>? DownloadRetry;

        private static HttpClient CreateClient()
        {
            var inner = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(30),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
            };

            var handler = new StallGuardHandler(inner)
            {
                IdleReadTimeout = TimeSpan.FromSeconds(20),
                MaxAttempts = 5
            };

            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        private sealed class StallGuardHandler : DelegatingHandler
        {
            private const long MaxBufferBytes = 8L * 1024 * 1024;

            public TimeSpan IdleReadTimeout { get; set; } = TimeSpan.FromSeconds(20);
            public int MaxAttempts { get; set; } = 5;

            public StallGuardHandler(HttpMessageHandler inner) : base(inner) { }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method != HttpMethod.Get)
                    return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                Exception? lastError = null;

                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var attemptRequest = CloneGet(request);
                    HttpResponseMessage? response = null;
                    try
                    {
                        response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);

                        if (IsRetryableStatus(response.StatusCode) && attempt < MaxAttempts)
                        {
                            response.Dispose();
                            NotifyRetry(request, attempt, $"HTTP {(int)response.StatusCode}");
                            await DelayBeforeRetry(attempt, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        if (!response.IsSuccessStatusCode)
                            return response;

                        long? length = response.Content.Headers.ContentLength;
                        if (length is null || length > MaxBufferBytes)
                            return await BuildStreamingResponse(response).ConfigureAwait(false);

                        byte[] body = await ReadWithIdleTimeout(response, cancellationToken).ConfigureAwait(false);
                        return BuildBufferedResponse(response, body);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        response?.Dispose();
                        throw;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
                    {
                        response?.Dispose();
                        lastError = ex;
                        if (attempt < MaxAttempts)
                        {
                            NotifyRetry(request, attempt, Lang.T("обрыв соединения"));
                            await DelayBeforeRetry(attempt, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                throw new HttpRequestException(
                    Lang.F("Не удалось скачать {0} за {1} попыток.", request.RequestUri?.ToString() ?? "", MaxAttempts), lastError);
            }

            private async Task<byte[]> ReadWithIdleTimeout(HttpResponseMessage response, CancellationToken ct)
            {
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                long? expected = response.Content.Headers.ContentLength;
                using var buffer = expected is > 0 and < int.MaxValue
                    ? new MemoryStream((int)expected.Value)
                    : new MemoryStream();

                byte[] chunk = new byte[65536];
                while (true)
                {
                    using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idleCts.CancelAfter(IdleReadTimeout);

                    int read;
                    try
                    {
                        read = await stream.ReadAsync(chunk, 0, chunk.Length, idleCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new IOException(
                            Lang.F("Соединение зависло: нет данных дольше {0:F0} с.", IdleReadTimeout.TotalSeconds));
                    }

                    if (read == 0)
                        break;

                    buffer.Write(chunk, 0, read);
                }

                return buffer.ToArray();
            }

            private async Task<HttpResponseMessage> BuildStreamingResponse(HttpResponseMessage original)
            {
                var networkStream = await original.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var guarded = new IdleTimeoutStream(networkStream, original, IdleReadTimeout);

                var message = new HttpResponseMessage(original.StatusCode)
                {
                    Version = original.Version,
                    ReasonPhrase = original.ReasonPhrase,
                    RequestMessage = original.RequestMessage
                };

                foreach (var header in original.Headers)
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);

                var content = new StreamContent(guarded);
                foreach (var header in original.Content.Headers)
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                message.Content = content;

                return message;
            }

            private static HttpResponseMessage BuildBufferedResponse(HttpResponseMessage original, byte[] body)
            {
                var buffered = new HttpResponseMessage(original.StatusCode)
                {
                    Version = original.Version,
                    ReasonPhrase = original.ReasonPhrase,
                    RequestMessage = original.RequestMessage
                };

                foreach (var header in original.Headers)
                    buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);

                var content = new ByteArrayContent(body);
                if (original.Content != null)
                {
                    foreach (var header in original.Content.Headers)
                    {
                        if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                            continue;
                        content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
                content.Headers.ContentLength = body.Length;
                buffered.Content = content;

                original.Dispose();
                return buffered;
            }

            private static void NotifyRetry(HttpRequestMessage request, int attempt, string reason)
            {
                var subscribers = DownloadRetry;
                if (subscribers == null)
                    return;

                string fileName = request.RequestUri is { } uri
                    ? Path.GetFileName(uri.AbsolutePath)
                    : Lang.T("файл");
                if (string.IsNullOrEmpty(fileName))
                    fileName = Lang.T("файл");

                subscribers.Invoke(Lang.F("Повтор: {0} (попытка {1}, {2})", fileName, attempt + 1, reason));
            }

            private static HttpRequestMessage CloneGet(HttpRequestMessage request)
            {
                var clone = new HttpRequestMessage(HttpMethod.Get, request.RequestUri)
                {
                    Version = request.Version
                };

                foreach (var header in request.Headers)
                    clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

                return clone;
            }

            private static Task DelayBeforeRetry(int attempt, CancellationToken ct)
                => Task.Delay(TimeSpan.FromMilliseconds(Math.Min(2000, 250 * attempt)), ct);

            private static bool IsRetryableStatus(HttpStatusCode status) => status switch
            {
                HttpStatusCode.RequestTimeout => true,
                (HttpStatusCode)429 => true,
                HttpStatusCode.InternalServerError => true,
                HttpStatusCode.BadGateway => true,
                HttpStatusCode.ServiceUnavailable => true,
                HttpStatusCode.GatewayTimeout => true,
                _ => false
            };
        }

        private sealed class IdleTimeoutStream : Stream
        {
            private readonly Stream _inner;
            private readonly HttpResponseMessage _response;
            private readonly TimeSpan _idleTimeout;

            public IdleTimeoutStream(Stream inner, HttpResponseMessage response, TimeSpan idleTimeout)
            {
                _inner = inner;
                _response = response;
                _idleTimeout = idleTimeout;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                idleCts.CancelAfter(_idleTimeout);
                try
                {
                    return await _inner.ReadAsync(buffer, idleCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new IOException(
                        Lang.F("Соединение зависло: нет данных дольше {0:F0} с.", _idleTimeout.TotalSeconds));
                }
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override int Read(byte[] buffer, int offset, int count)
                => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                    _response.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
