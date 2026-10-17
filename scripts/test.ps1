. "$PSScriptRoot/tooling.ps1"
Push-Location $repoRoot
try {
    Invoke-Dotnet restore PaperProductTool.sln --configfile NuGet.Config
    Invoke-Dotnet test tests/Paper.Api.Tests/Paper.Api.Tests.csproj --no-restore -m:1 /nodeReuse:false
    Push-Location frontend
    try { Invoke-Npm ci --no-fund --no-audit; Invoke-Npm test; Invoke-Npm run build } finally { Pop-Location }
} finally { Pop-Location }
