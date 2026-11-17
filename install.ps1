$ErrorActionPreference = "Stop"

$pluginTargetDir = Join-Path $env:APPDATA "FlowLauncher\Plugins\Google Search Recommendations-1.0.0"
$releaseDir = Join-Path $PSScriptRoot "src\Flow.Launcher.Plugin.GoogleSearchRecommendations\bin\Release"

if (-not (Test-Path $releaseDir)) {
    throw "Release build output not found at $releaseDir. Run 'dotnet build -c Release' first."
}

New-Item -ItemType Directory -Force -Path $pluginTargetDir | Out-Null
Copy-Item -Path "$releaseDir\*" -Destination $pluginTargetDir -Recurse -Force

Write-Host "Successfully installed Google Search Recommendations plugin to: $pluginTargetDir"
Write-Host "Restart Flow Launcher (or type 'Reload Plugin Data' in Flow Launcher) to start using 'g <query>'!"
