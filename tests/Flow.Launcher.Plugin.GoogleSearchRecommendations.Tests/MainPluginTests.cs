using Xunit;

namespace Flow.Launcher.Plugin.GoogleSearchRecommendations.Tests;

public class MainPluginTests
{
    private sealed class FakeSuggestService : IGoogleSuggestService
    {
        private readonly IReadOnlyList<string> _suggestions;

        public FakeSuggestService(params string[] suggestions)
        {
            _suggestions = suggestions;
        }

        public Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_suggestions);
        }
    }

    private sealed class BlockingSuggestService : IGoogleSuggestService
    {
        private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _handler;

        public BlockingSuggestService(Func<string, CancellationToken, Task<IReadOnlyList<string>>> handler)
        {
            _handler = handler;
        }

        public Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default)
        {
            return _handler(query, cancellationToken);
        }
    }

    private static Query CreateQuery(string search, string actionKeyword)
    {
        string raw = string.IsNullOrEmpty(actionKeyword) ? search : $"{actionKeyword} {search}";
        var query = new Query
        {
            ActionKeyword = actionKeyword,
            SearchTerms = string.IsNullOrWhiteSpace(search)
                ? Array.Empty<string>()
                : search.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
        };

        typeof(Query).GetProperty(nameof(Query.RawQuery))!.SetValue(query, raw.Trim());
        typeof(Query).GetProperty(nameof(Query.Search))!.SetValue(query, search);
        return query;
    }

    [Fact]
    public async Task QueryAsync_EmptySearchWithKeyword_ReturnsDefaultSearchGooglePrompt()
    {
        string? openedUrl = null;
        var plugin = new Main(
            new FakeSuggestService("should not be returned"),
            urlOpener: url => openedUrl = url);

        List<Result> results = await plugin.QueryAsync(search: "   ", actionKeyword: "g", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Search Google", results[0].Title);
        Assert.Equal("Images\\google.png", results[0].IcoPath);
        Assert.Equal(1000, results[0].Score);

        bool invoked = results[0].Action!(new ActionContext());
        Assert.True(invoked);
        Assert.Equal("https://www.google.com", openedUrl);
    }

    [Fact]
    public async Task QueryAsync_EmptySearchWithGlobalWildcard_ReturnsEmptyList()
    {
        var plugin = new Main(
            new FakeSuggestService("should not be returned"),
            urlOpener: _ => { });

        List<Result> results = await plugin.QueryAsync(search: "", actionKeyword: "", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_StandardQuery_ReturnsPrimaryRowFollowedByOrderedRecommendations()
    {
        var openedUrls = new List<string>();
        var plugin = new Main(
            new FakeSuggestService("how to bake a cake", "how to bake salmon", "how to bake cookies"),
            urlOpener: url => openedUrls.Add(url));

        List<Result> results = await plugin.QueryAsync(search: "how to bake", actionKeyword: "g", CancellationToken.None);

        Assert.Equal(4, results.Count);

        // Row 0: Primary exact search
        Assert.Equal("how to bake", results[0].Title);
        Assert.Equal("Search Google for \"how to bake\"", results[0].SubTitle);
        Assert.Equal("Images\\google.png", results[0].IcoPath);
        Assert.Equal(1000, results[0].Score);
        Assert.Equal("g how to bake ", results[0].AutoCompleteText);

        // Rows 1..3: Recommendations with descending scores and magnifying glass icon
        Assert.Equal("how to bake a cake", results[1].Title);
        Assert.Equal("Google Search Suggestion", results[1].SubTitle);
        Assert.Equal("Images\\search.png", results[1].IcoPath);
        Assert.Equal(900, results[1].Score);
        Assert.Equal("g how to bake a cake ", results[1].AutoCompleteText);

        Assert.Equal("how to bake salmon", results[2].Title);
        Assert.Equal("Images\\search.png", results[2].IcoPath);
        Assert.Equal(899, results[2].Score);
        Assert.Equal("g how to bake salmon ", results[2].AutoCompleteText);

        Assert.Equal("how to bake cookies", results[3].Title);
        Assert.Equal("Images\\search.png", results[3].IcoPath);
        Assert.Equal(898, results[3].Score);
        Assert.Equal("g how to bake cookies ", results[3].AutoCompleteText);

        // Verify clicking primary row opens exact search URL
        results[0].Action!(new ActionContext());
        Assert.Equal("https://www.google.com/search?q=how%20to%20bake", openedUrls[0]);

        // Verify clicking recommendation row opens recommended search URL
        results[1].Action!(new ActionContext());
        Assert.Equal("https://www.google.com/search?q=how%20to%20bake%20a%20cake", openedUrls[1]);
    }

    [Fact]
    public async Task QueryAsync_DomainOrUrlInput_PrependsDirectUrlRowAbovePrimaryAndRecommendations()
    {
        string? openedUrl = null;
        var plugin = new Main(
            new FakeSuggestService("github.com login", "github.com desktop"),
            urlOpener: url => openedUrl = url);

        List<Result> results = await plugin.QueryAsync(search: "github.com", actionKeyword: "g", CancellationToken.None);

        Assert.Equal(4, results.Count);

        // Top row: Open URL directly
        Assert.Equal("Open https://github.com/", results[0].Title);
        Assert.Equal("Open URL directly in browser", results[0].SubTitle);
        Assert.Equal("Images\\google.png", results[0].IcoPath);
        Assert.Equal(1100, results[0].Score);

        results[0].Action!(new ActionContext());
        Assert.Equal("https://github.com/", openedUrl);

        // Second row: Primary Google Search
        Assert.Equal("github.com", results[1].Title);
        Assert.Equal(1000, results[1].Score);

        // Third & Fourth rows: Google Recommendations
        Assert.Equal("github.com login", results[2].Title);
        Assert.Equal(900, results[2].Score);
        Assert.Equal("github.com desktop", results[3].Title);
        Assert.Equal(899, results[3].Score);
    }

    [Fact]
    public async Task QueryAsync_GlobalWildcardKeyword_FormatsAutoCompleteWithoutPrefix()
    {
        var plugin = new Main(
            new FakeSuggestService("weather tomorrow"),
            urlOpener: _ => { });

        List<Result> results = await plugin.QueryAsync(search: "weather", actionKeyword: "", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("weather ", results[0].AutoCompleteText);
        Assert.Equal("weather tomorrow ", results[1].AutoCompleteText);
    }

    [Fact]
    public async Task QueryAsync_SlowSuggestions_PublishesPrimaryResultImmediatelyViaResultsUpdated()
    {
        var suggestGate = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? openedUrl = null;
        var plugin = new Main(
            new BlockingSuggestService((_, ct) => suggestGate.Task.WaitAsync(ct)),
            urlOpener: url => openedUrl = url);

        var updater = (object)plugin as IResultUpdated;
        Assert.NotNull(updater);

        ResultUpdatedEventArgs? immediateArgs = null;
        updater!.ResultsUpdated += (_, e) => immediateArgs = e;

        using var cts = new CancellationTokenSource();
        Query query = CreateQuery("is nullity and nullability same", "");
        Task<List<Result>> queryTask = plugin.QueryAsync(query, cts.Token);

        // Suggestions are still pending, yet the primary result must already be published.
        Assert.False(queryTask.IsCompleted);
        Assert.NotNull(immediateArgs);
        Assert.Same(query, immediateArgs!.Query);
        Assert.Equal(cts.Token, immediateArgs.Token);
        Assert.Single(immediateArgs.Results);
        Assert.Equal("is nullity and nullability same", immediateArgs.Results[0].Title);
        Assert.Equal(1000, immediateArgs.Results[0].Score);

        immediateArgs.Results[0].Action!(new ActionContext());
        Assert.Equal("https://www.google.com/search?q=is%20nullity%20and%20nullability%20same", openedUrl);

        // Now complete suggestions and verify final list contains primary + suggestions
        // without mutating the earlier immediate snapshot.
        suggestGate.SetResult(new[] { "is nullity and null space the same" });
        List<Result> finalResults = await queryTask;

        Assert.Single(immediateArgs.Results);
        Assert.Equal(2, finalResults.Count);
        Assert.Equal("is nullity and nullability same", finalResults[0].Title);
        Assert.Equal("is nullity and null space the same", finalResults[1].Title);
    }

    [Fact]
    public async Task QueryAsync_FastTypingCancelsIntermediateSuggestions_PublishesLatestPrimaryBeforeNetworkCompletes()
    {
        var pendingGates = new List<TaskCompletionSource<IReadOnlyList<string>>>();
        var suggestService = new BlockingSuggestService((q, ct) =>
        {
            if (q == "is nullity and nullabili")
            {
                return Task.FromResult<IReadOnlyList<string>>(new[] { "is nullity and null space the same" });
            }

            var tcs = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingGates.Add(tcs);
            return tcs.Task.WaitAsync(ct);
        });

        string? openedUrl = null;
        var plugin = new Main(suggestService, urlOpener: url => openedUrl = url);
        var updater = (object)plugin as IResultUpdated;
        Assert.NotNull(updater);

        List<Result>? displayedResults = null;
        Query? currentQuery = null;
        updater!.ResultsUpdated += (_, e) =>
        {
            if (currentQuery is not null && e.Query.RawQuery == currentQuery.RawQuery && !e.Token.IsCancellationRequested)
            {
                displayedResults = e.Results;
            }
        };

        // Initial prefix finishes suggestions normally
        using var cts1 = new CancellationTokenSource();
        currentQuery = CreateQuery("is nullity and nullabili", "");
        displayedResults = await plugin.QueryAsync(currentQuery, cts1.Token);
        Assert.Equal("is nullity and nullabili", displayedResults[0].Title);

        // User rapidly types the rest of the sentence; every network request stays pending
        string[] rapidPrefixes =
        {
            "is nullity and nullabilit",
            "is nullity and nullability",
            "is nullity and nullability s",
            "is nullity and nullability same"
        };

        CancellationTokenSource? previousCts = cts1;
        Task<List<Result>>? previousTask = null;

        foreach (string prefix in rapidPrefixes)
        {
            previousCts.Cancel();
            if (previousTask is not null)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => previousTask);
            }

            var nextCts = new CancellationTokenSource();
            currentQuery = CreateQuery(prefix, "");
            previousTask = plugin.QueryAsync(currentQuery, nextCts.Token);
            previousCts = nextCts;
        }

        try
        {
            // User presses Enter BEFORE final network suggestions arrive
            Assert.NotNull(displayedResults);
            Assert.Equal("is nullity and nullability same", displayedResults![0].Title);

            displayedResults[0].Action!(new ActionContext());
            Assert.Equal("https://www.google.com/search?q=is%20nullity%20and%20nullability%20same", openedUrl);
        }
        finally
        {
            previousCts.Cancel();
            if (previousTask is not null)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => previousTask);
            }
            previousCts.Dispose();
        }
    }

    [Fact]
    public async Task QueryAsync_DomainWithSlowSuggestions_PublishesDirectUrlAndPrimaryImmediately()
    {
        var suggestGate = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new Main(
            new BlockingSuggestService((_, ct) => suggestGate.Task.WaitAsync(ct)),
            urlOpener: _ => { });

        var updater = (object)plugin as IResultUpdated;
        Assert.NotNull(updater);

        ResultUpdatedEventArgs? immediateArgs = null;
        updater!.ResultsUpdated += (_, e) => immediateArgs = e;

        using var cts = new CancellationTokenSource();
        Query query = CreateQuery("github.com", "g");
        Task<List<Result>> queryTask = plugin.QueryAsync(query, cts.Token);

        Assert.False(queryTask.IsCompleted);
        Assert.NotNull(immediateArgs);
        Assert.Equal(2, immediateArgs!.Results.Count);
        Assert.Equal("Open https://github.com/", immediateArgs.Results[0].Title);
        Assert.Equal(1100, immediateArgs.Results[0].Score);
        Assert.Equal("github.com", immediateArgs.Results[1].Title);
        Assert.Equal(1000, immediateArgs.Results[1].Score);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queryTask);
    }

    private sealed class CachingFakeSuggestService : IGoogleSuggestService
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _cache = new(StringComparer.OrdinalIgnoreCase);
        public int AsyncCallCount { get; private set; }

        public void SeedCache(string query, params string[] suggestions)
        {
            _cache[query.Trim()] = suggestions;
        }

        public bool TryGetCachedSuggestions(string query, out IReadOnlyList<string> suggestions)
        {
            if (!string.IsNullOrWhiteSpace(query) && _cache.TryGetValue(query.Trim(), out IReadOnlyList<string>? cached))
            {
                suggestions = cached;
                return true;
            }

            suggestions = Array.Empty<string>();
            return false;
        }

        public Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AsyncCallCount++;
            return Task.FromResult<IReadOnlyList<string>>(
                _cache.TryGetValue(query.Trim(), out IReadOnlyList<string>? cached)
                    ? cached
                    : Array.Empty<string>());
        }
    }

    [Fact]
    public async Task QueryAsync_CachedSuggestions_ReturnsImmediatelyWithoutDebounceOrPartialUpdate()
    {
        var suggestService = new CachingFakeSuggestService();
        suggestService.SeedCache("how to bake", "how to bake a cake", "how to bake salmon");

        var plugin = new Main(
            suggestService,
            urlOpener: _ => { },
            suggestionDebounce: TimeSpan.FromSeconds(5));

        int resultsUpdatedCount = 0;
        plugin.ResultsUpdated += (_, _) => resultsUpdatedCount++;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        List<Result> results = await plugin.QueryAsync(CreateQuery("how to bake", "g"), timeoutCts.Token);

        Assert.Equal(0, resultsUpdatedCount);
        Assert.Equal(0, suggestService.AsyncCallCount);
        Assert.Equal(3, results.Count);
        Assert.Equal("how to bake", results[0].Title);
        Assert.Equal("how to bake a cake", results[1].Title);
        Assert.Equal("how to bake salmon", results[2].Title);
    }

    [Fact]
    public async Task QueryAsync_FastTypingWithDebounce_SkipsNetworkRequestsForCancelledIntermediateQueries()
    {
        var requestedQueries = new List<string>();
        var suggestService = new BlockingSuggestService((q, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            lock (requestedQueries)
            {
                requestedQueries.Add(q);
            }
            return Task.FromResult<IReadOnlyList<string>>(new[] { $"{q} suggestion" });
        });

        var plugin = new Main(
            suggestService,
            urlOpener: _ => { },
            suggestionDebounce: TimeSpan.FromMilliseconds(120));

        var immediateTitles = new List<string>();
        plugin.ResultsUpdated += (_, e) => immediateTitles.Add(e.Results[0].Title);

        string[] prefixes = { "is null", "is nulli", "is nullit", "is nullity" };
        CancellationTokenSource? previousCts = null;
        Task<List<Result>>? previousTask = null;

        for (int i = 0; i < prefixes.Length; i++)
        {
            if (previousCts is not null)
            {
                previousCts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => previousTask!);
                previousCts.Dispose();
            }

            previousCts = new CancellationTokenSource();
            previousTask = plugin.QueryAsync(CreateQuery(prefixes[i], ""), previousCts.Token);

            if (i < prefixes.Length - 1)
            {
                await Task.Delay(15);
            }
        }

        List<Result> finalResults = await previousTask!;
        previousCts!.Dispose();

        // Every single prefix published its primary row immediately,
        // yet only the final settled query hit the suggest service.
        Assert.Equal(prefixes, immediateTitles);
        Assert.Single(requestedQueries);
        Assert.Equal("is nullity", requestedQueries[0]);
        Assert.Equal(2, finalResults.Count);
        Assert.Equal("is nullity suggestion", finalResults[1].Title);
    }

    [Fact]
    public async Task QueryAsync_StalePrimaryResultExecutedAfterNewerQuery_SearchesLatestQuery()
    {
        var pendingGate = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var suggestService = new BlockingSuggestService((q, ct) =>
        {
            if (q == "is nullity and nullabili")
            {
                return Task.FromResult<IReadOnlyList<string>>(new[] { "is nullity and null space the same" });
            }

            return pendingGate.Task.WaitAsync(ct);
        });

        string? openedUrl = null;
        var plugin = new Main(suggestService, urlOpener: url => openedUrl = url);

        using var cts1 = new CancellationTokenSource();
        List<Result> staleResults = await plugin.QueryAsync(CreateQuery("is nullity and nullabili", ""), cts1.Token);

        // User keeps typing: Flow Launcher cancels cts1 and dispatches the newer query,
        // but Enter arrives before Flow Launcher's 20ms result channel updates the UI.
        cts1.Cancel();
        using var cts2 = new CancellationTokenSource();
        Task<List<Result>> newerQueryTask = plugin.QueryAsync(CreateQuery("is nullity and nullability same", ""), cts2.Token);

        staleResults[0].Action!(new ActionContext());

        Assert.Equal("https://www.google.com/search?q=is%20nullity%20and%20nullability%20same", openedUrl);

        cts2.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => newerQueryTask);
    }

    [Fact]
    public async Task QueryAsync_StaleTopRowBecomesUrlInNewerQuery_OpensDirectUrl()
    {
        var pendingGate = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var suggestService = new BlockingSuggestService((q, ct) =>
            q == "github.c"
                ? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>())
                : pendingGate.Task.WaitAsync(ct));

        string? openedUrl = null;
        var plugin = new Main(suggestService, urlOpener: url => openedUrl = url);

        using var cts1 = new CancellationTokenSource();
        List<Result> staleResults = await plugin.QueryAsync(CreateQuery("github.c", "g"), cts1.Token);
        Assert.Single(staleResults);

        cts1.Cancel();
        using var cts2 = new CancellationTokenSource();
        Task<List<Result>> newerQueryTask = plugin.QueryAsync(CreateQuery("github.com", "g"), cts2.Token);

        staleResults[0].Action!(new ActionContext());

        Assert.Equal("https://github.com/", openedUrl);

        cts2.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => newerQueryTask);
    }

    [Fact]
    public async Task QueryAsync_StaleDirectUrlRowExecutedAfterNewerQuery_UpdatesUrlOrFallsBackToSearch()
    {
        var pendingGate = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var suggestService = new BlockingSuggestService((q, ct) =>
            q == "https://example.com/pa" || q == "github.com"
                ? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>())
                : pendingGate.Task.WaitAsync(ct));

        string? openedUrl = null;
        var plugin = new Main(suggestService, urlOpener: url => openedUrl = url);

        // Case 1: URL extended to a longer URL
        using var cts1 = new CancellationTokenSource();
        List<Result> staleUrlResults = await plugin.QueryAsync(CreateQuery("https://example.com/pa", "g"), cts1.Token);

        cts1.Cancel();
        using var cts2 = new CancellationTokenSource();
        Task<List<Result>> newerUrlTask = plugin.QueryAsync(CreateQuery("https://example.com/page", "g"), cts2.Token);

        staleUrlResults[0].Action!(new ActionContext());
        Assert.Equal("https://example.com/page", openedUrl);

        cts2.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => newerUrlTask);

        // Case 2: Domain extended with words (no longer a URL -> falls back to Google Search)
        using var cts3 = new CancellationTokenSource();
        List<Result> staleDomainResults = await plugin.QueryAsync(CreateQuery("github.com", "g"), cts3.Token);

        cts3.Cancel();
        using var cts4 = new CancellationTokenSource();
        Task<List<Result>> newerSearchTask = plugin.QueryAsync(CreateQuery("github.com pricing", "g"), cts4.Token);

        staleDomainResults[0].Action!(new ActionContext());
        Assert.Equal("https://www.google.com/search?q=github.com%20pricing", openedUrl);

        cts4.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => newerSearchTask);
    }

    [Fact]
    public async Task QueryAsync_StaleEmptyKeywordPromptExecutedAfterNewerQuery_SearchesLatestQuery()
    {
        var pendingGate = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? openedUrl = null;
        var plugin = new Main(
            new BlockingSuggestService((_, ct) => pendingGate.Task.WaitAsync(ct)),
            urlOpener: url => openedUrl = url);

        using var cts1 = new CancellationTokenSource();
        List<Result> emptyPromptResults = await plugin.QueryAsync(CreateQuery("", "g"), cts1.Token);
        Assert.Equal("Search Google", emptyPromptResults[0].Title);

        cts1.Cancel();
        using var cts2 = new CancellationTokenSource();
        Task<List<Result>> newerQueryTask = plugin.QueryAsync(CreateQuery("cats", "g"), cts2.Token);

        emptyPromptResults[0].Action!(new ActionContext());
        Assert.Equal("https://www.google.com/search?q=cats", openedUrl);

        cts2.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => newerQueryTask);
    }

    [Fact]
    public async Task QueryAsync_StaleSuggestionRowExecuted_StillSearchesSelectedSuggestion()
    {
        var pendingGate = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var suggestService = new BlockingSuggestService((q, ct) =>
            q == "is nullity"
                ? Task.FromResult<IReadOnlyList<string>>(new[] { "is nullity and null space the same" })
                : pendingGate.Task.WaitAsync(ct));

        string? openedUrl = null;
        var plugin = new Main(suggestService, urlOpener: url => openedUrl = url);

        using var cts1 = new CancellationTokenSource();
        List<Result> staleResults = await plugin.QueryAsync(CreateQuery("is nullity", ""), cts1.Token);

        cts1.Cancel();
        using var cts2 = new CancellationTokenSource();
        Task<List<Result>> newerQueryTask = plugin.QueryAsync(CreateQuery("is nullity and", ""), cts2.Token);

        staleResults[1].Action!(new ActionContext());
        Assert.Equal("https://www.google.com/search?q=is%20nullity%20and%20null%20space%20the%20same", openedUrl);

        cts2.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => newerQueryTask);
    }

    [Fact]
    public async Task QueryAsync_StalePrimaryResultBeforeNewQueryDispatched_UsesLiveRawQueryProviderWithKeyword()
    {
        string liveRawQuery = "g is nullity and nullabili";
        string? openedUrl = null;
        var plugin = new Main(
            new FakeSuggestService(),
            urlOpener: url => openedUrl = url,
            currentRawQueryProvider: () => liveRawQuery);

        using var cts1 = new CancellationTokenSource();
        List<Result> staleResults = await plugin.QueryAsync(CreateQuery("is nullity and nullabili", "g"), cts1.Token);

        // User finished typing and Flow Launcher cancelled cts1 synchronously on the UI thread,
        // then Enter fired BEFORE QueryAsync was even invoked for the new text (e.g. SearchDelayTime).
        liveRawQuery = "g is nullity and nullability same";
        cts1.Cancel();

        staleResults[0].Action!(new ActionContext());

        Assert.Equal("https://www.google.com/search?q=is%20nullity%20and%20nullability%20same", openedUrl);
    }

    [Fact]
    public async Task QueryAsync_StalePrimaryResultBeforeNewQueryDispatched_UsesLiveRawQueryProviderWithGlobalKeyword()
    {
        string liveRawQuery = "is nullity and nullabili";
        string? openedUrl = null;
        var plugin = new Main(
            new FakeSuggestService(),
            urlOpener: url => openedUrl = url,
            currentRawQueryProvider: () => liveRawQuery);

        using var cts1 = new CancellationTokenSource();
        List<Result> staleResults = await plugin.QueryAsync(CreateQuery("is nullity and nullabili", ""), cts1.Token);

        liveRawQuery = "is nullity and nullability same";
        cts1.Cancel();

        staleResults[0].Action!(new ActionContext());

        Assert.Equal("https://www.google.com/search?q=is%20nullity%20and%20nullability%20same", openedUrl);
    }

    [Fact]
    public async Task QueryAsync_StalePrimaryResultWithDifferentActionKeywordInLiveBox_DoesNotHijackOtherPlugin()
    {
        string liveRawQuery = "g cats";
        string? openedUrl = null;
        var plugin = new Main(
            new FakeSuggestService(),
            urlOpener: url => openedUrl = url,
            currentRawQueryProvider: () => liveRawQuery);

        using var cts1 = new CancellationTokenSource();
        List<Result> staleResults = await plugin.QueryAsync(CreateQuery("cats", "g"), cts1.Token);

        liveRawQuery = "calc 2+2";
        cts1.Cancel();

        staleResults[0].Action!(new ActionContext());

        Assert.Equal("https://www.google.com/search?q=cats", openedUrl);
    }

    [Fact]
    public async Task QueryAsync_NonStalePrimaryRowBelowDirectUrl_StillSearchesGoogle()
    {
        string? openedUrl = null;
        var plugin = new Main(
            new FakeSuggestService("github.com login"),
            urlOpener: url => openedUrl = url);

        using var cts = new CancellationTokenSource();
        List<Result> results = await plugin.QueryAsync(CreateQuery("github.com", "g"), cts.Token);

        results[1].Action!(new ActionContext());

        Assert.Equal("https://www.google.com/search?q=github.com", openedUrl);
    }

    [Fact]
    public async Task QueryAsync_LiveGoogleSuggestEndpoint_ReturnsRealSuggestions()
    {
        var plugin = new Main();
        List<Result> results = await plugin.QueryAsync(search: "flow launcher", actionKeyword: "g", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("flow launcher", results[0].Title);
        Assert.Equal("Images\\google.png", results[0].IcoPath);
        Assert.True(results.Count > 1, "Expected live Google autocomplete recommendations below primary row.");
        Assert.All(results.Skip(1), r =>
        {
            Assert.Equal("Google Search Suggestion", r.SubTitle);
            Assert.Equal("Images\\search.png", r.IcoPath);
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
        });
    }
}
