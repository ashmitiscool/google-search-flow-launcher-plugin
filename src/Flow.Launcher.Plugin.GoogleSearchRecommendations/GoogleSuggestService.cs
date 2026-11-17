using System.Collections.Concurrent;
using System.Text.Json;

namespace Flow.Launcher.Plugin.GoogleSearchRecommendations;

public interface IGoogleSuggestService
{
    Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default);
}

public sealed class GoogleSuggestService : IGoogleSuggestService
{
    private const string SuggestEndpoint = "https://suggestqueries.google.com/complete/search?client=firefox&q=";
    private const int MaxCacheEntries = 500;

    private readonly HttpClient _httpClient;
    private readonly int _maxSuggestions;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public GoogleSuggestService(HttpClient? httpClient = null, int maxSuggestions = 8)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _maxSuggestions = maxSuggestions > 0 ? maxSuggestions : 8;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(2)
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; FlowLauncher)");
        return client;
    }

    public async Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        string trimmed = query.Trim();

        if (_cache.TryGetValue(trimmed, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        string requestUrl = SuggestEndpoint + Uri.EscapeDataString(trimmed);

        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<string>();
            }

            await using Stream contentStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using JsonDocument doc = await JsonDocument
                .ParseAsync(contentStream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
            {
                return Array.Empty<string>();
            }

            JsonElement suggestionsElement = root[1];
            if (suggestionsElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var results = new List<string>(_maxSuggestions);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { trimmed };

            foreach (JsonElement item in suggestionsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? suggestion = item.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(suggestion))
                {
                    continue;
                }

                if (seen.Add(suggestion))
                {
                    results.Add(suggestion);
                    if (results.Count >= _maxSuggestions)
                    {
                        break;
                    }
                }
            }

            if (_cache.Count >= MaxCacheEntries)
            {
                _cache.Clear();
            }

            _cache[trimmed] = results;
            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
