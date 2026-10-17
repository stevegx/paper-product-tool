$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.local/dotnet'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.local/nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $repoRoot '.local/nuget-cache'
$env:npm_config_cache = Join-Path $repoRoot '.local/npm-cache'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_NOLOGO = '1'
$resolvedDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetExecutable = if ($resolvedDotnet) { $resolvedDotnet.Source } else { 'C:/Program Files/dotnet/dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnetExecutable)) { throw 'Install the .NET 10 SDK first.' }
$resolvedNpm = Get-Command npm.cmd -ErrorAction SilentlyContinue
if (-not $resolvedNpm) { throw 'Install Node.js 22.12+ (or 24 LTS) with npm first.' }
$npmExecutable = $resolvedNpm.Source
function Invoke-Dotnet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CommandArguments)
    & $dotnetExecutable @CommandArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)." }
}
function Invoke-Npm {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CommandArguments)
    & $npmExecutable @CommandArguments
    if ($LASTEXITCODE -ne 0) { throw "npm failed ($LASTEXITCODE)." }
}
