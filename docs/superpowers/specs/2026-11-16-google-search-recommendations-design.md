# Flow Launcher Google Search Recommendations Plugin — Design Spec

## 1. Overview & Goal
Build a native C# Flow Launcher plugin (`Flow.Launcher.Plugin.GoogleSearchRecommendations`) that provides Google Search functionality alongside live Google autocomplete recommendations in Flow Launcher's results dropdown, mirroring browser omnibox search behavior.

## 2. Architecture & Runtime
- **Target Framework**: `net7.0-windows` (matching Flow Launcher v2.1.0 plugin runtime).
- **SDK Package**: `Flow.Launcher.Plugin` v4.5.0.
- **Entry Point**: `Main : IAsyncPlugin` in namespace `Flow.Launcher.Plugin.GoogleSearchRecommendations`.
- **Default Action Keyword**: `g` (configurable to `*` or any keyword in Flow Launcher settings).
- **Concurrency & Latency**:
  - Singleton `HttpClient` with `SocketsHttpHandler` connection pooling to keep TLS connections to `suggestqueries.google.com` warm across keystrokes.
  - Propagates Flow Launcher's per-keystroke `CancellationToken` to `HttpClient.GetAsync` so superseded keystrokes cancel immediately.
  - Thread-safe bounded in-memory cache (`ConcurrentDictionary` / LRU) so backspacing or retyping recent prefixes resolves in `0 ms`.

## 3. Data Flow & Result Hierarchy
1. **Empty Query (`g `)**:
   - Returns 1 result:
     - `Title`: `"Search Google"`
     - `SubTitle`: `"Type a query to search Google with live recommendations"`
     - `IcoPath`: `"Images\\google.png"`
     - `Score`: `1000`
     - `Action`: Opens `https://www.google.com` via `context.API.OpenUrl`.
2. **Direct URL / Domain Detection (`UrlDetector.TryGetUrl(query, out string normalizedUrl)`)**:
   - If the typed query is a valid HTTP(S) URL or bare domain (e.g. `github.com`, `https://openai.com/blog`, `docs.microsoft.com/en-us`):
   - Prepends a top result:
     - `Title`: `"Open {normalizedUrl}"`
     - `SubTitle`: `"Open URL directly in browser"`
     - `IcoPath`: `"Images\\google.png"`
     - `Score`: `1100`
     - `AutoCompleteText`: Formatted with current action keyword (`"g {query}"` or `"{query}"` if global `*`)
     - `Action`: Opens `normalizedUrl` via `context.API.OpenUrl`.
3. **Primary Google Search Row**:
   - Always present for any non-empty query:
     - `Title`: `"{trimmedQuery}"`
     - `SubTitle`: `"Search Google for \"{trimmedQuery}\""`
     - `IcoPath`: `"Images\\google.png"`
     - `Score`: `1000`
     - `AutoCompleteText`: Formatted with current action keyword (`"g {trimmedQuery} "` or `"{trimmedQuery} "` if global `*`)
     - `Action`: Opens `https://www.google.com/search?q={Uri.EscapeDataString(trimmedQuery)}` via `context.API.OpenUrl`.
4. **Live Google Autocomplete Recommendation Rows**:
   - Fetched from `https://suggestqueries.google.com/complete/search?client=firefox&q={Uri.EscapeDataString(trimmedQuery)}`.
   - Response format is JSON array: `["query", ["suggestion 1", "suggestion 2", ...], ...]`.
   - Deduplicates any suggestion that matches `trimmedQuery` case-insensitively (`StringComparer.OrdinalIgnoreCase`).
   - Up to 8 suggestions rendered below the primary row:
     - `Title`: `"{suggestion}"`
     - `SubTitle`: `"Google Search Suggestion"`
     - `IcoPath`: `"Images\\search.png"`
     - `Score`: `900 - index` (preserves Google's exact ranking order)
     - `AutoCompleteText`: Formatted with current action keyword (`"g {suggestion} "` or `"{suggestion} "` if global `*`)
     - `Action`: Opens `https://www.google.com/search?q={Uri.EscapeDataString(suggestion)}` via `context.API.OpenUrl`.

## 4. Error & Edge-Case Handling
- **Cancellation (`OperationCanceledException`)**: Returns empty list or rethrows cleanly when token is cancelled by a newer keystroke.
- **Network / HTTP Failure / Timeout**: Swallows network errors gracefully and still returns the Primary Google Search row (and URL row if applicable) so the user can always press `Enter` to search even when offline.
