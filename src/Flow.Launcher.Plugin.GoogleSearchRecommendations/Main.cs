using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Flow.Launcher.Plugin.GoogleSearchRecommendations;

public sealed class Main : IAsyncPlugin, IResultUpdated
{
    private sealed record LatestQuerySnapshot(long Sequence, string ActionKeyword, string TrimmedSearch, CancellationToken Token);

    private const string GoogleHomeUrl = "https://www.google.com";
    private const string GoogleSearchUrlPrefix = "https://www.google.com/search?q=";
    private const string GoogleIconPath = "Images\\google.png";
    private const string SearchIconPath = "Images\\search.png";
    private static readonly TimeSpan DefaultSuggestionDebounce = TimeSpan.FromMilliseconds(80);

    private readonly IGoogleSuggestService _suggestService;
    private readonly Action<string>? _customUrlOpener;
    private readonly TimeSpan _suggestionDebounce;
    private readonly Func<string?> _currentRawQueryProvider;
    private readonly object _latestQueryLock = new();
    private long _querySequence;
    private LatestQuerySnapshot? _latestQuery;
    private PluginInitContext? _context;

    public event ResultUpdatedEventHandler? ResultsUpdated;

    public Main()
        : this(new GoogleSuggestService(), null, DefaultSuggestionDebounce, null)
    {
    }

    public Main(
        IGoogleSuggestService suggestService,
        Action<string>? urlOpener = null,
        TimeSpan? suggestionDebounce = null,
        Func<string?>? currentRawQueryProvider = null)
    {
        _suggestService = suggestService ?? throw new ArgumentNullException(nameof(suggestService));
        _customUrlOpener = urlOpener;
        _suggestionDebounce = suggestionDebounce is { } delay && delay > TimeSpan.Zero
            ? delay
            : TimeSpan.Zero;
        _currentRawQueryProvider = currentRawQueryProvider ?? TryGetLiveQueryTextFromHost;
    }

    public Task InitAsync(PluginInitContext context)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        if (query is null)
        {
            return Task.FromResult(new List<Result>());
        }

        return QueryInternalAsync(query, query.Search ?? string.Empty, query.ActionKeyword ?? string.Empty, token);
    }

    public Task<List<Result>> QueryAsync(string search, string actionKeyword, CancellationToken token)
    {
        return QueryInternalAsync(null, search ?? string.Empty, actionKeyword ?? string.Empty, token);
    }

    private async Task<List<Result>> QueryInternalAsync(Query? sourceQuery, string search, string actionKeyword, CancellationToken token)
    {
        string normalizedActionKeyword = actionKeyword ?? string.Empty;
        string trimmedSearch = search?.Trim() ?? string.Empty;
        long querySequence = RecordLatestQuery(normalizedActionKeyword, trimmedSearch, token);

        if (string.IsNullOrEmpty(trimmedSearch))
        {
            if (IsGlobalKeyword(normalizedActionKeyword))
            {
                return new List<Result>();
            }

            return new List<Result>
            {
                new()
                {
                    Title = "Search Google",
                    SubTitle = "Type a query to search Google with live recommendations",
                    IcoPath = GoogleIconPath,
                    Score = 1000,
                    Action = _ =>
                    {
                        if (token.IsCancellationRequested &&
                            TryResolveLatestSearch(querySequence, normalizedActionKeyword, out string latestSearch))
                        {
                            OpenSearchOrUrl(latestSearch, preferDirectUrl: true);
                            return true;
                        }

                        OpenUrl(GoogleHomeUrl);
                        return true;
                    }
                }
            };
        }

        var results = new List<Result>(10);
        bool hasDirectUrl = UrlDetector.TryGetUrl(trimmedSearch, out string normalizedUrl);

        if (hasDirectUrl)
        {
            results.Add(new Result
            {
                Title = $"Open {normalizedUrl}",
                SubTitle = "Open URL directly in browser",
                IcoPath = GoogleIconPath,
                Score = 1100,
                AutoCompleteText = FormatAutoCompleteText(normalizedActionKeyword, trimmedSearch, appendTrailingSpace: false),
                Action = _ =>
                {
                    if (token.IsCancellationRequested &&
                        TryResolveLatestSearch(querySequence, normalizedActionKeyword, out string latestSearch))
                    {
                        OpenSearchOrUrl(latestSearch, preferDirectUrl: true);
                        return true;
                    }

                    OpenUrl(normalizedUrl);
                    return true;
                }
            });
        }

        string primarySearchUrl = GoogleSearchUrlPrefix + Uri.EscapeDataString(trimmedSearch);
        bool isTopRow = !hasDirectUrl;
        results.Add(new Result
        {
            Title = trimmedSearch,
            SubTitle = $"Search Google for \"{trimmedSearch}\"",
            IcoPath = GoogleIconPath,
            Score = 1000,
            AutoCompleteText = FormatAutoCompleteText(normalizedActionKeyword, trimmedSearch, appendTrailingSpace: true),
            Action = _ =>
            {
                if (token.IsCancellationRequested &&
                    TryResolveLatestSearch(querySequence, normalizedActionKeyword, out string latestSearch))
                {
                    OpenSearchOrUrl(latestSearch, preferDirectUrl: isTopRow);
                    return true;
                }

                OpenUrl(primarySearchUrl);
                return true;
            }
        });

        bool isReQuery = sourceQuery?.IsReQuery == true;
        if (!isReQuery && _suggestService.TryGetCachedSuggestions(trimmedSearch, out IReadOnlyList<string> cachedSuggestions))
        {
            AppendSuggestions(results, cachedSuggestions, normalizedActionKeyword);
            return results;
        }

        // Publish immediate results (direct URL + primary exact search) before waiting on network suggestions
        // so fast typing + Enter never executes a stale result from an earlier incomplete prefix.
        token.ThrowIfCancellationRequested();
        ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs
        {
            Results = new List<Result>(results),
            Query = sourceQuery ?? new Query { ActionKeyword = normalizedActionKeyword },
            Token = token
        });

        if (_suggestionDebounce > TimeSpan.Zero)
        {
            await Task.Delay(_suggestionDebounce, token).ConfigureAwait(false);
        }

        IReadOnlyList<string> suggestions = await _suggestService
            .GetSuggestionsAsync(trimmedSearch, token)
            .ConfigureAwait(false);

        token.ThrowIfCancellationRequested();

        AppendSuggestions(results, suggestions, normalizedActionKeyword);
        return results;
    }

    private long RecordLatestQuery(string actionKeyword, string trimmedSearch, CancellationToken token)
    {
        lock (_latestQueryLock)
        {
            token.ThrowIfCancellationRequested();
            long sequence = ++_querySequence;
            _latestQuery = new LatestQuerySnapshot(sequence, actionKeyword, trimmedSearch, token);
            return sequence;
        }
    }

    private bool TryResolveLatestSearch(long capturedSequence, string capturedActionKeyword, out string latestSearch)
    {
        LatestQuerySnapshot? snapshot;
        lock (_latestQueryLock)
        {
            snapshot = _latestQuery;
        }

        // 1. If Flow Launcher has already dispatched the latest active query to this plugin
        //    (and no newer keystroke has cancelled it), trust Flow Launcher's parsed Search directly.
        if (snapshot is not null &&
            snapshot.Sequence > capturedSequence &&
            !snapshot.Token.IsCancellationRequested &&
            !string.IsNullOrEmpty(snapshot.TrimmedSearch) &&
            HasCompatibleActionKeyword(capturedActionKeyword, snapshot.ActionKeyword))
        {
            latestSearch = snapshot.TrimmedSearch;
            return true;
        }

        // 2. If Enter raced ahead of QueryAsync dispatch (e.g. ThreadPool scheduling or SearchDelayTime),
        //    read the live query box text directly from the host window.
        string? liveRawQuery = _currentRawQueryProvider();
        if (!string.IsNullOrWhiteSpace(liveRawQuery) &&
            TryExtractSearchForPlugin(liveRawQuery, capturedActionKeyword, out string liveSearch))
        {
            latestSearch = liveSearch;
            return true;
        }

        // 3. Fallback to any newer query snapshot already received by this plugin.
        if (snapshot is not null &&
            snapshot.Sequence > capturedSequence &&
            !string.IsNullOrEmpty(snapshot.TrimmedSearch) &&
            HasCompatibleActionKeyword(capturedActionKeyword, snapshot.ActionKeyword))
        {
            latestSearch = snapshot.TrimmedSearch;
            return true;
        }

        latestSearch = string.Empty;
        return false;
    }

    private bool TryExtractSearchForPlugin(string rawQuery, string capturedActionKeyword, out string extractedSearch)
    {
        extractedSearch = string.Empty;
        string trimmedRaw = rawQuery.Trim();
        if (string.IsNullOrEmpty(trimmedRaw))
        {
            return false;
        }

        int firstSpace = trimmedRaw.IndexOf(' ');
        string firstTerm = firstSpace >= 0 ? trimmedRaw[..firstSpace] : trimmedRaw;

        if (!IsGlobalKeyword(capturedActionKeyword))
        {
            if (firstSpace < 0 || !string.Equals(firstTerm, capturedActionKeyword, StringComparison.Ordinal))
            {
                return false;
            }

            string remainder = trimmedRaw[(firstSpace + 1)..].Trim();
            if (string.IsNullOrEmpty(remainder))
            {
                return false;
            }

            extractedSearch = remainder;
            return true;
        }

        if (firstSpace >= 0)
        {
            if (IsOwnNonGlobalActionKeyword(firstTerm))
            {
                string remainder = trimmedRaw[(firstSpace + 1)..].Trim();
                if (string.IsNullOrEmpty(remainder))
                {
                    return false;
                }

                extractedSearch = remainder;
                return true;
            }

            if (_context?.API?.ActionKeywordAssigned(firstTerm) == true)
            {
                return false;
            }
        }
        else if (IsOwnNonGlobalActionKeyword(firstTerm) || _context?.API?.ActionKeywordAssigned(firstTerm) == true)
        {
            return false;
        }

        extractedSearch = trimmedRaw;
        return true;
    }

    private bool IsOwnNonGlobalActionKeyword(string term)
    {
        List<string>? keywords = _context?.CurrentPluginMetadata?.ActionKeywords;
        if (keywords is null)
        {
            return false;
        }

        foreach (string keyword in keywords)
        {
            if (!IsGlobalKeyword(keyword) && string.Equals(keyword, term, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCompatibleActionKeyword(string first, string second)
    {
        if (IsGlobalKeyword(first) && IsGlobalKeyword(second))
        {
            return true;
        }

        return string.Equals(first, second, StringComparison.Ordinal);
    }

    private static string? TryGetLiveQueryTextFromHost()
    {
        try
        {
            Application? app = Application.Current;
            if (app is null)
            {
                return null;
            }

            string? ReadQueryText()
            {
                Window? mainWindow = app.MainWindow;
                if (mainWindow is null)
                {
                    return null;
                }

                if (mainWindow.FindName("QueryTextBox") is TextBox textBox)
                {
                    return textBox.Text;
                }

                object? dataContext = mainWindow.DataContext;
                return dataContext?.GetType().GetProperty("QueryText")?.GetValue(dataContext) as string;
            }

            return app.Dispatcher.CheckAccess()
                ? ReadQueryText()
                : app.Dispatcher.Invoke(ReadQueryText, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromMilliseconds(25));
        }
        catch
        {
            return null;
        }
    }

    private void OpenSearchOrUrl(string searchText, bool preferDirectUrl)
    {
        if (preferDirectUrl && UrlDetector.TryGetUrl(searchText, out string directUrl))
        {
            OpenUrl(directUrl);
            return;
        }

        OpenUrl(GoogleSearchUrlPrefix + Uri.EscapeDataString(searchText));
    }

    private void AppendSuggestions(List<Result> results, IReadOnlyList<string> suggestions, string actionKeyword)
    {
        for (int i = 0; i < suggestions.Count; i++)
        {
            string suggestion = suggestions[i];
            string suggestionUrl = GoogleSearchUrlPrefix + Uri.EscapeDataString(suggestion);

            results.Add(new Result
            {
                Title = suggestion,
                SubTitle = "Google Search Suggestion",
                IcoPath = SearchIconPath,
                Score = 900 - i,
                AutoCompleteText = FormatAutoCompleteText(actionKeyword, suggestion, appendTrailingSpace: true),
                Action = _ =>
                {
                    OpenUrl(suggestionUrl);
                    return true;
                }
            });
        }
    }

    private static bool IsGlobalKeyword(string? actionKeyword)
    {
        return string.IsNullOrWhiteSpace(actionKeyword) || actionKeyword == Query.GlobalPluginWildcardSign;
    }

    private static string FormatAutoCompleteText(string? actionKeyword, string queryText, bool appendTrailingSpace)
    {
        string suffix = appendTrailingSpace ? " " : string.Empty;
        if (IsGlobalKeyword(actionKeyword))
        {
            return $"{queryText}{suffix}";
        }

        return $"{actionKeyword} {queryText}{suffix}";
    }

    private void OpenUrl(string url)
    {
        if (_customUrlOpener is not null)
        {
            _customUrlOpener(url);
            return;
        }

        if (_context?.API is not null)
        {
            _context.API.OpenUrl(url);
            return;
        }

        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
}
