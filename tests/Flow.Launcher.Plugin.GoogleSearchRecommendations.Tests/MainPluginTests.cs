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
