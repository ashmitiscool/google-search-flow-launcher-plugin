using Xunit;

namespace Flow.Launcher.Plugin.GoogleSearchRecommendations.Tests;

public class UrlDetectorTests
{
    [Theory]
    [InlineData("https://www.google.com", "https://www.google.com/")]
    [InlineData("http://example.com/test?q=1", "http://example.com/test?q=1")]
    [InlineData("github.com", "https://github.com/")]
    [InlineData("docs.microsoft.com/en-us/dotnet", "https://docs.microsoft.com/en-us/dotnet")]
    [InlineData("sub.domain.co.in", "https://sub.domain.co.in/")]
    [InlineData("localhost:3000", "http://localhost:3000/")]
    [InlineData("localhost:8080/api/health", "http://localhost:8080/api/health")]
    public void TryGetUrl_ValidUrlsAndDomains_ReturnsTrueWithNormalizedUrl(string input, string expectedUrl)
    {
        bool isUrl = UrlDetector.TryGetUrl(input, out string normalizedUrl);

        Assert.True(isUrl);
        Assert.Equal(expectedUrl, normalizedUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("how to make pancakes")]
    [InlineData("flow launcher plugin")]
    [InlineData("c# .net")]
    [InlineData("node.js tutorial")]
    [InlineData("version 2.5")]
    [InlineData("google")]
    public void TryGetUrl_SearchQueries_ReturnsFalse(string input)
    {
        bool isUrl = UrlDetector.TryGetUrl(input, out string normalizedUrl);

        Assert.False(isUrl);
        Assert.Equal(string.Empty, normalizedUrl);
    }
}
