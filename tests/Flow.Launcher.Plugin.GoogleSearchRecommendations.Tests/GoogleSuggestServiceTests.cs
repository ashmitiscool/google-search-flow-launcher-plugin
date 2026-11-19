using System.Net;
using System.Text;
using Xunit;

namespace Flow.Launcher.Plugin.GoogleSearchRecommendations.Tests;

public class GoogleSuggestServiceTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
        public int CallCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            return _responder(request, cancellationToken);
        }
    }

    [Fact]
    public async Task GetSuggestionsAsync_ParsesFirefoxFormatAndDeduplicatesExactQuery()
    {
        const string jsonResponse = """
            ["how to bake",["how to bake","How To Bake","how to bake a cake","how to bake chicken breast","how to bake salmon","how to bake potatoes"]]
            """;

        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        }));

        var service = new GoogleSuggestService(new HttpClient(handler));

        IReadOnlyList<string> results = await service.GetSuggestionsAsync("how to bake");

        Assert.Equal(4, results.Count);
        Assert.Equal("how to bake a cake", results[0]);
        Assert.Equal("how to bake chicken breast", results[1]);
        Assert.Equal("how to bake salmon", results[2]);
        Assert.Equal("how to bake potatoes", results[3]);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Contains("client=firefox", handler.LastRequestUri!.Query);
        Assert.Contains("q=how%20to%20bake", handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task GetSuggestionsAsync_CachesResponsesCaseInsensitively()
    {
        const string jsonResponse = """
            ["flow launcher",["flow launcher plugins","flow launcher themes"]]
            """;

        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        }));

        var service = new GoogleSuggestService(new HttpClient(handler));

        var first = await service.GetSuggestionsAsync("flow launcher");
        var second = await service.GetSuggestionsAsync("Flow Launcher");

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task TryGetCachedSuggestions_ReturnsFalseBeforeFetchAndTrueCaseInsensitivelyAfterFetch()
    {
        const string jsonResponse = """
            ["flow launcher",["flow launcher plugins","flow launcher themes"]]
            """;

        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        }));

        var service = new GoogleSuggestService(new HttpClient(handler));

        Assert.False(service.TryGetCachedSuggestions("flow launcher", out IReadOnlyList<string> beforeFetch));
        Assert.Empty(beforeFetch);

        IReadOnlyList<string> fetched = await service.GetSuggestionsAsync("flow launcher");

        Assert.True(service.TryGetCachedSuggestions("  FLOW LAUNCHER  ", out IReadOnlyList<string> cached));
        Assert.Equal(fetched, cached);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetSuggestionsAsync_CapsAtMaxSuggestions()
    {
        const string jsonResponse = """
            ["test",["test 1","test 2","test 3","test 4","test 5","test 6","test 7","test 8","test 9","test 10"]]
            """;

        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        }));

        var service = new GoogleSuggestService(new HttpClient(handler), maxSuggestions: 8);

        var results = await service.GetSuggestionsAsync("test");

        Assert.Equal(8, results.Count);
        Assert.Equal("test 1", results[0]);
        Assert.Equal("test 8", results[7]);
    }

    [Fact]
    public async Task GetSuggestionsAsync_NetworkErrorOrBadJson_ReturnsEmptyListGracefully()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Simulated offline network failure"));

        var service = new GoogleSuggestService(new HttpClient(handler));

        var results = await service.GetSuggestionsAsync("offline query");

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetSuggestionsAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new StubHttpMessageHandler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var service = new GoogleSuggestService(new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetSuggestionsAsync("cancelled query", cts.Token));
    }
}
