using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CustomLauncher.Core
{
    public sealed record ChangelogEntry(string Id, string Title, string Body);

    public sealed class ChangelogFeed
    {
        public const string LauncherFeed = "launcher";
        public const string ModpackFeed = "modpack";
        public const string FallbackLanguage = "ru";

        private const string BaseUrl = "https://raw.githubusercontent.com/pers1k1/vrsns/main/changelog/";
        private const int MaxIndexBytes = 64 * 1024;
        private const int MaxEntryBytes = 32 * 1024;
        private const int MaxIndexedEntries = 2000;
        private static readonly TimeSpan RequestDeadline = TimeSpan.FromSeconds(15);
        private static readonly Regex EntryIdPattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly HttpClient _http;
        private readonly string _feed;
        private readonly string _language;
        private IReadOnlyList<string>? _newestFirstIds;
        private int _loadedCount;

        public ChangelogFeed(HttpClient http, string feed, string language)
        {
            _http = http;
            _feed = feed;
            _language = language;
        }

        public bool HasMore => _newestFirstIds == null || _loadedCount < _newestFirstIds.Count;

        public int Total => _newestFirstIds?.Count ?? 0;

        public async Task<IReadOnlyList<ChangelogEntry>> NextPageAsync(int pageSize, CancellationToken token)
        {
            _newestFirstIds ??= await LoadIndexAsync(token);
            List<string> pageIds = _newestFirstIds.Skip(_loadedCount).Take(pageSize).ToList();
            ChangelogEntry[] page = await Task.WhenAll(pageIds.Select(id => LoadEntryAsync(id, token)));
            _loadedCount += pageIds.Count;
            return page;
        }

        public static IReadOnlyList<string> ParseIndex(string text)
        {
            List<string> oldestFirst = Normalize(text)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(IsValidEntryId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            oldestFirst.Reverse();
            return oldestFirst.Take(MaxIndexedEntries).ToList();
        }

        public static bool IsValidEntryId(string id) => EntryIdPattern.IsMatch(id) && !id.Contains("..");

        public static ChangelogEntry ParseEntry(string id, string text)
        {
            string normalized = Normalize(text).Trim();
            int titleEnd = normalized.IndexOf('\n');
            string title = (titleEnd < 0 ? normalized : normalized[..titleEnd]).Trim();
            string body = titleEnd < 0 ? "" : normalized[(titleEnd + 1)..].Trim('\n', ' ', '\t');
            return new ChangelogEntry(id, title.Length > 0 ? title : DisplayTitle(id), body);
        }

        private async Task<IReadOnlyList<string>> LoadIndexAsync(CancellationToken token)
        {
            string url = $"{BaseUrl}{_feed}/index.txt?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            string text = await GetCappedAsync(url, MaxIndexBytes, token);
            IReadOnlyList<string> ids = ParseIndex(text);
            LauncherLog.Write($"[NET] Ченджлог {_feed}: в индексе {ids.Count} записей, язык {_language}");
            return ids;
        }

        private async Task<ChangelogEntry> LoadEntryAsync(string id, CancellationToken token)
        {
            try
            {
                return ParseEntry(id, await GetEntryTextAsync(id, token));
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                LauncherLog.Write($"[NET] Ченджлог {_feed}: запись {id} не загрузилась: {error.Message}");
                return new ChangelogEntry(id, DisplayTitle(id), Lang.T("Не удалось загрузить эту запись."));
            }
        }

        private async Task<string> GetEntryTextAsync(string id, CancellationToken token)
        {
            try
            {
                return await GetCappedAsync(EntryUrl(id, _language), MaxEntryBytes, token);
            }
            catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound && _language != FallbackLanguage)
            {
                LauncherLog.Write($"[NET] Ченджлог {_feed}: у записи {id} нет перевода {_language}, показан {FallbackLanguage}");
                return await GetCappedAsync(EntryUrl(id, FallbackLanguage), MaxEntryBytes, token);
            }
        }

        private string EntryUrl(string id, string language) => $"{BaseUrl}{_feed}/{id}.{language}.txt";

        private async Task<string> GetCappedAsync(string url, int maxBytes, CancellationToken token)
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(RequestDeadline);
            using HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            byte[] buffer = new byte[maxBytes];
            int total = 0;
            int read;
            while (total < maxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), deadline.Token)) > 0)
                total += read;
            string text = Encoding.UTF8.GetString(buffer, 0, total);
            return total < maxBytes ? text : text.TrimEnd('�') + "\n…";
        }

        private static string Normalize(string text) => (text ?? "").TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');

        private static string DisplayTitle(string id) => ReleaseVersion.IsValid(id) ? ReleaseVersion.Display(id) : id;
    }
}
