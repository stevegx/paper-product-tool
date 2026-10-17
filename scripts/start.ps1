param([string]$ExcelPath = '', [int]$Year = 2026, [switch]$SkipBuild)
. "$PSScriptRoot/tooling.ps1"
Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        Push-Location frontend
        try { Invoke-Npm ci --no-fund --no-audit; Invoke-Npm run build } finally { Pop-Location }
    }
    Invoke-Dotnet restore backend/Paper.Api --configfile NuGet.Config
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    if ($ExcelPath) {
        $env:Import__Path = (Resolve-Path -LiteralPath $ExcelPath).Path
        $env:Import__Year = "$Year"
    }
    Write-Host "Starting .NET. Wait for 'Now listening on: http://127.0.0.1:5080' before opening that address."
    Invoke-Dotnet run --project backend/Paper.Api --no-restore --no-launch-profile --urls http://127.0.0.1:5080
} finally { Pop-Location }
