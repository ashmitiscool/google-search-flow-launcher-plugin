# Google Search Recommendations Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build, test, package, and install the C# Flow Launcher plugin `Flow.Launcher.Plugin.GoogleSearchRecommendations` that shows live Google autocomplete suggestions below the primary Google search action.

**Architecture:** A `net7.0-windows` C# library implementing `Flow.Launcher.Plugin.IAsyncPlugin` (`Main.cs`), backed by a testable `GoogleSuggestService` (`HttpClient` + `CancellationToken` + in-memory cache) and `UrlDetector` (domain/URL recognizer), tested with xUnit (`Flow.Launcher.Plugin.GoogleSearchRecommendations.Tests`).

**Tech Stack:** C# 11, `.NET 7.0` (`net7.0-windows`), `Flow.Launcher.Plugin` 4.5.0, `System.Text.Json`, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-16-google-search-recommendations-design.md`

## Global Constraints
- Target framework: `net7.0-windows`
- `Flow.Launcher.Plugin` version: `4.5.0`
- Default action keyword: `g`
- Primary row uses `Images\google.png`, recommendation rows use `Images\search.png`
- Zero external runtime dependencies beyond `Flow.Launcher.Plugin` and .NET 7 base class library

---

### Task 1: Project Scaffolding & Icons
- [x] Create solution `Flow.Launcher.Plugin.GoogleSearchRecommendations.sln`
- [x] Create plugin project `src/Flow.Launcher.Plugin.GoogleSearchRecommendations/Flow.Launcher.Plugin.GoogleSearchRecommendations.csproj`
- [x] Create test project `tests/Flow.Launcher.Plugin.GoogleSearchRecommendations.Tests/Flow.Launcher.Plugin.GoogleSearchRecommendations.Tests.csproj`
- [x] Create `plugin.json` metadata and `Images/google.png` + `Images/search.png` icons

### Task 2: `UrlDetector` (TDD)
- [x] Write failing unit tests in `tests/Flow.Launcher.Plugin.GoogleSearchRecommendations.Tests/UrlDetectorTests.cs` covering full URLs (`https://...`), bare domains (`github.com`, `sub.domain.co.uk/path`), localhost/ports, and plain search queries (`how to bake a cake`, `c# async await`).
- [x] Run tests to verify RED.
- [x] Implement `src/Flow.Launcher.Plugin.GoogleSearchRecommendations/UrlDetector.cs`.
- [x] Run tests to verify GREEN.

### Task 3: `GoogleSuggestService` (TDD)
- [x] Write failing unit tests in `tests/Flow.Launcher.Plugin.GoogleSearchRecommendations.Tests/GoogleSuggestServiceTests.cs` covering JSON parsing of `client=firefox` payload, deduplication, caching, cancellation, and graceful fallback on HTTP/network errors.
- [x] Run tests to verify RED.
- [x] Implement `src/Flow.Launcher.Plugin.GoogleSearchRecommendations/GoogleSuggestService.cs`.
- [x] Run tests to verify GREEN.

### Task 4: `Main` Plugin Query & Result Builder (TDD)
- [x] Write failing unit tests in `tests/Flow.Launcher.Plugin.GoogleSearchRecommendations.Tests/MainPluginTests.cs` covering:
  - Empty query returns default "Search Google" prompt
  - Standard query returns primary search row (score 1000, `Images\google.png`) + deduplicated recommendation rows (scores 900.., `Images\search.png`)
  - `AutoCompleteText` formatting for both `g` keyword and `*` global keyword
  - Direct URL query prepends `Open <url>` row (score 1100)
- [x] Run tests to verify RED.
- [x] Implement `src/Flow.Launcher.Plugin.GoogleSearchRecommendations/Main.cs`.
- [x] Run tests to verify GREEN.

### Task 5: Release Packaging & Local Installation into Flow Launcher
- [x] Publish release artifacts (`dotnet build -c Release`)
- [x] Package `dist/GoogleSearchRecommendations-1.0.0.zip`
- [x] Deploy plugin to `%APPDATA%\FlowLauncher\Plugins\Google Search Recommendations-1.0.0`
