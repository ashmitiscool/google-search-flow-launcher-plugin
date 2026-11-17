using System.Diagnostics;

namespace Flow.Launcher.Plugin.GoogleSearchRecommendations;

public sealed class Main : IAsyncPlugin
{
    private const string GoogleHomeUrl = "https://www.google.com";
    private const string GoogleSearchUrlPrefix = "https://www.google.com/search?q=";
    private const string GoogleIconPath = "Images\\google.png";
    private const string SearchIconPath = "Images\\search.png";

    private readonly IGoogleSuggestService _suggestService;
    private readonly Action<string>? _customUrlOpener;
    private PluginInitContext? _context;

    public Main()
        : this(new GoogleSuggestService(), null)
    {
    }

    public Main(IGoogleSuggestService suggestService, Action<string>? urlOpener = null)
    {
        _suggestService = suggestService ?? throw new ArgumentNullException(nameof(suggestService));
        _customUrlOpener = urlOpener;
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

        return QueryAsync(query.Search ?? string.Empty, query.ActionKeyword ?? string.Empty, token);
    }

    public async Task<List<Result>> QueryAsync(string search, string actionKeyword, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        string trimmedSearch = search?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(trimmedSearch))
        {
            if (IsGlobalKeyword(actionKeyword))
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
                        OpenUrl(GoogleHomeUrl);
                        return true;
                    }
                }
            };
        }

        var results = new List<Result>(10);

        if (UrlDetector.TryGetUrl(trimmedSearch, out string normalizedUrl))
        {
            results.Add(new Result
            {
                Title = $"Open {normalizedUrl}",
                SubTitle = "Open URL directly in browser",
                IcoPath = GoogleIconPath,
                Score = 1100,
                AutoCompleteText = FormatAutoCompleteText(actionKeyword, trimmedSearch, appendTrailingSpace: false),
                Action = _ =>
                {
                    OpenUrl(normalizedUrl);
                    return true;
                }
            });
        }

        string primarySearchUrl = GoogleSearchUrlPrefix + Uri.EscapeDataString(trimmedSearch);
        results.Add(new Result
        {
            Title = trimmedSearch,
            SubTitle = $"Search Google for \"{trimmedSearch}\"",
            IcoPath = GoogleIconPath,
            Score = 1000,
            AutoCompleteText = FormatAutoCompleteText(actionKeyword, trimmedSearch, appendTrailingSpace: true),
            Action = _ =>
            {
                OpenUrl(primarySearchUrl);
                return true;
            }
        });

        IReadOnlyList<string> suggestions = await _suggestService
            .GetSuggestionsAsync(trimmedSearch, token)
            .ConfigureAwait(false);

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

        return results;
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
