# Flow.Launcher.Plugin.GoogleSearchRecommendations

Native C# (`net7.0-windows`, `Flow.Launcher.Plugin` 4.5.0) `IAsyncPlugin` for Flow Launcher that surfaces live Google Search autocomplete recommendations and direct URL/domain actions under the `g` keyword (or global `*`).

## Commands

- **Build (Release):** `dotnet build -c Release`
- **Test (all):** `dotnet test`
- **Install locally:** `.\install.ps1` (requires a prior `dotnet build -c Release`; copies output to `%APPDATA%\FlowLauncher\Plugins\Google Search Recommendations-1.0.0`)

## Architecture

- `Flow.Launcher.Plugin.GoogleSearchRecommendations.sln`: Solution linking the plugin library and xUnit test project.
- `src/Flow.Launcher.Plugin.GoogleSearchRecommendations/Main.cs`: `IAsyncPlugin` entry point (`Main`). Builds ranked Flow Launcher `Result` items: direct URL (`Score = 1100` via `UrlDetector`), primary Google Search (`Score = 1000`), and up to 8 autocomplete suggestions (`Score = 900 - i`). Provides a `(IGoogleSuggestService, Action<string>?)` constructor for unit testing without `PluginInitContext`.
- `src/Flow.Launcher.Plugin.GoogleSearchRecommendations/GoogleSuggestService.cs`: Defines `IGoogleSuggestService` and `GoogleSuggestService`. Calls `https://suggestqueries.google.com/complete/search?client=firefox&q=...` using `SocketsHttpHandler` (`ConnectTimeout = 2s`, `Timeout = 3s`), caches up to 500 queries case-insensitively in a `ConcurrentDictionary`, deduplicates suggestions against the trimmed query, propagates caller `CancellationToken` cancellations, and returns an empty list on HTTP/JSON failures.
- `src/Flow.Launcher.Plugin.GoogleSearchRecommendations/UrlDetector.cs`: Static `UrlDetector.TryGetUrl` recognizing explicit `http://`/`https://` URLs, `localhost:<port>`, and bare domains (rejecting whitespace), returning normalized `Uri.AbsoluteUri`.
- `src/Flow.Launcher.Plugin.GoogleSearchRecommendations/plugin.json` & `Images/`: Flow Launcher manifest (`ActionKeywords: ["g"]`, `Version: "1.0.0"`) and icons (`Images\google.png`, `Images\search.png`), copied to build output via `PreserveNewest`.
- `tests/Flow.Launcher.Plugin.GoogleSearchRecommendations.Tests/`: xUnit suite (`MainPluginTests.cs`, `GoogleSuggestServiceTests.cs`, `UrlDetectorTests.cs`) using in-memory `FakeSuggestService` and `StubHttpMessageHandler`, plus one live-endpoint test (`QueryAsync_LiveGoogleSuggestEndpoint_ReturnsRealSuggestions`).

## Conventions & Gotchas

- **Output Path Layout:** `Flow.Launcher.Plugin.GoogleSearchRecommendations.csproj` sets `AppendTargetFrameworkToOutputPath=false` and `CopyLocalLockFileAssemblies=true`. Do not remove these—`install.ps1` expects binaries directly under `src\Flow.Launcher.Plugin.GoogleSearchRecommendations\bin\Release\`.
- **Version Alignment:** Keep `plugin.json` `Version` (`1.0.0`) synchronized with the target plugin directory name in `install.ps1` (`Google Search Recommendations-1.0.0`).
- **Global Keyword Behavior:** When `actionKeyword` is blank or `Query.GlobalPluginWildcardSign`, `Main` returns no results for an empty search and omits the keyword prefix in `AutoCompleteText`.
- **Live Network Dependency in Tests:** `MainPluginTests.QueryAsync_LiveGoogleSuggestEndpoint_ReturnsRealSuggestions` calls `suggestqueries.google.com` over the network and will fail in offline environments.
- **Gitignored Outputs:** `bin/`, `obj/`, `.vs/`, and `dist/` (e.g., `dist\GoogleSearchRecommendations-1.0.0.zip`) are excluded by `.gitignore`.
