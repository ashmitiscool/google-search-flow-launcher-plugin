# Flow.Launcher.Plugin.GoogleSearchRecommendations

A native C# (`IAsyncPlugin`) plugin for [Flow Launcher](https://www.flowlauncher.com/) that brings live browser-style Google Search autocomplete recommendations directly into Flow Launcher's results dropdown.

## Features

- **Primary Google Search + Live Recommendations**: Type `g <query>` to see your exact search query on top (`Images/google.png`), immediately followed by live Google autocomplete recommendations (`Images/search.png`) below it in exact relevance order.
- **Direct URL / Domain Recognition**: Typing a domain or URL (e.g. `g github.com` or `g https://news.ycombinator.com`) prepends an `Open <url>` action at the top while keeping Google Search and recommendations right below.
- **Zero Per-Keystroke Process Overhead**: Runs natively in-process on `.NET 7` (`IAsyncPlugin`) with a pooled `HttpClient`, per-keystroke `CancellationToken` propagation, and an in-memory cache so backspacing or retyping is instantaneous.
- **Keep Typing with Autocomplete**: Use Flow Launcher's autocomplete hotkey (`Ctrl+Tab` or `Tab` depending on your Flow Launcher configuration) on any recommendation to fill it into the search bar and continue refining your search.

## Usage

| Input | Result |
| :--- | :--- |
| `g ` | Shows **Search Google** prompt (opens `https://www.google.com`) |
| `g how to bake` | Top row searches Google for `"how to bake"`; rows below show live suggestions (`how to bake a cake`, `how to bake salmon`, etc.) |
| `g github.com` | Top row opens `https://github.com/` directly; rows below search Google and show autocomplete suggestions |

## Installation

### Option 1: Install Script
Run the included PowerShell installer to copy the Release build directly into `%APPDATA%\FlowLauncher\Plugins\Google Search Recommendations-1.0.0`:
```powershell
.\install.ps1
```
Then restart Flow Launcher (or run **Reload Plugin Data** inside Flow Launcher).

### Option 2: Release Zip
Extract `dist/GoogleSearchRecommendations-1.0.0.zip` into `%APPDATA%\FlowLauncher\Plugins\Google Search Recommendations-1.0.0`.

## Development & Testing

```powershell
dotnet test
dotnet build -c Release
```
