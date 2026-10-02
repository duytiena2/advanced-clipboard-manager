<#
.SYNOPSIS
  Build, test, run or publish Advanced Clipboard Manager on Windows.

.EXAMPLE
  .\build.ps1                 # restore + build + run core tests
  .\build.ps1 -Run            # build and start the app (look for the tray icon)
  .\build.ps1 -Publish        # self-contained single-file exe in .\publish\
#>
param(
    [switch]$Run,
    [switch]$Publish,
    [switch]$SkipTests,
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release',
    # Official SQLite DLL (includes FTS5). Override if this version is no longer hosted.
    [string]$SqliteUrl = 'https://www.sqlite.org/2025/sqlite-dll-win-x64-3500400.zip'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

# 1. .NET 8 SDK
Step 'Checking .NET SDK'
function Find-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $true }
    # Installed but this PowerShell window has an old PATH
    $default = Join-Path $env:ProgramFiles 'dotnet'
    if (Test-Path (Join-Path $default 'dotnet.exe')) {
        $env:PATH = "$default;$env:PATH"
        return $true
    }
    return $false
}

if (-not (Find-Dotnet)) {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw ".NET 8 SDK not found and winget is unavailable. Download 'SDK 8.0 - Windows x64' from https://dotnet.microsoft.com/download/dotnet/8.0, install it, then run .\build.ps1 again."
    }
    Write-Host '.NET 8 SDK not found - installing with winget (a UAC prompt may appear)...' -ForegroundColor Yellow
    & winget install --id Microsoft.DotNet.SDK.8 --exact --accept-source-agreements --accept-package-agreements
    if (-not (Find-Dotnet)) {
        throw "Installation did not complete (winget exit code $LASTEXITCODE). Install .NET 8 SDK manually from https://dotnet.microsoft.com/download/dotnet/8.0, then run .\build.ps1 again."
    }
}
$sdks = & dotnet --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^(8|9|10)\.' })) {
    throw ".NET 8 (or newer) SDK required. Found:`n$sdks`nInstall: winget install Microsoft.DotNet.SDK.8"
}
Write-Host ($sdks -join "`n")

# 2. sqlite3.dll (optional: without it the app uses Windows' winsqlite3.dll)
$lib = Join-Path $PSScriptRoot 'lib'
$dll = Join-Path $lib 'sqlite3.dll'
if (-not (Test-Path $dll)) {
    Step 'Downloading official sqlite3.dll (FTS5 full-text search)'
    New-Item -ItemType Directory -Force -Path $lib | Out-Null
    $zip = Join-Path $env:TEMP 'sqlite-dll-win-x64.zip'
    try {
        Invoke-WebRequest -Uri $SqliteUrl -OutFile $zip -UseBasicParsing
        Expand-Archive -Path $zip -DestinationPath $lib -Force
        Remove-Item $zip -ErrorAction SilentlyContinue
        Write-Host "sqlite3.dll saved to $lib"
    }
    catch {
        Write-Warning "Could not download sqlite3.dll ($($_.Exception.Message))."
        Write-Warning "The app will fall back to Windows' built-in winsqlite3.dll. To fix, download 'sqlite-dll-win-x64-*.zip' from https://www.sqlite.org/download.html and extract sqlite3.dll into .\lib\"
    }
}

# 3. Tests (Core logic: classification, storage, FTS5 search, expiration, merge...)
if (-not $SkipTests) {
    Step 'Running core tests'
    & dotnet run --project tests/ClipboardManager.Core.Tests -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

# 4. Build app
Step "Building app ($Configuration)"
& dotnet build src/ClipboardManager.App -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if ($Publish) {
    Step 'Publishing self-contained single-file exe'
    & dotnet publish src/ClipboardManager.App -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
    Write-Host "`nDone: $(Join-Path $PSScriptRoot 'publish\ClipboardManager.exe')" -ForegroundColor Green
}

if ($Run) {
    Step 'Starting Clipboard Manager (tray icon, Ctrl+Shift+V)'
    $exe = Get-ChildItem "src/ClipboardManager.App/bin/$Configuration" -Recurse -Filter ClipboardManager.exe | Select-Object -First 1
    if (-not $exe) { throw "ClipboardManager.exe not found after build." }
    Start-Process $exe.FullName
}

Write-Host "`nOK" -ForegroundColor Green
