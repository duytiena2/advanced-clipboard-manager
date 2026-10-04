<#
.SYNOPSIS
  Build, test, run or publish Advanced Clipboard Manager on Windows.

.EXAMPLE
  .\build.ps1                 # restore + build + run core tests
  .\build.ps1 -Run            # build and start the app (look for the tray icon)
  .\build.ps1 -Publish        # self-contained single-file exe in .\publish\
  .\build.ps1 -Installer      # Setup.exe in .\dist\ (Inno Setup; installed with winget if missing)
  .\build.ps1 -Msix           # Store package (.msix) in .\dist\
#>
param(
    [switch]$Run,
    [switch]$Publish,
    [switch]$Installer,
    [switch]$Msix,
    [switch]$SkipTests,
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release',
    # Official SQLite DLL (includes FTS5). Override if this version is no longer hosted.
    [string]$SqliteUrl = 'https://www.sqlite.org/2025/sqlite-dll-win-x64-3500400.zip',
    [string]$AppVersion = '',
    # MSIX identity. For the Store, use the values from Partner Center > Product identity
    # (CI reads them from the MSIX_* repository variables). The defaults only suit local testing.
    [string]$MsixIdentityName = $(if ($env:MSIX_IDENTITY_NAME) { $env:MSIX_IDENTITY_NAME } else { 'duytiena2.AdvancedClipboardManager' }),
    [string]$MsixPublisher = $(if ($env:MSIX_PUBLISHER) { $env:MSIX_PUBLISHER } else { 'CN=A524D558-6059-4A68-B81A-B8E8782556BB' }),
    [string]$MsixPublisherDisplayName = $(if ($env:MSIX_PUBLISHER_DISPLAY_NAME) { $env:MSIX_PUBLISHER_DISPLAY_NAME } else { 'duytiena2' }),
    [string]$MsixDisplayName = $(if ($env:MSIX_DISPLAY_NAME) { $env:MSIX_DISPLAY_NAME } else { 'Advanced Clipboard Manager' })
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
# A running copy locks bin\...\*.dll, so stop instances started from this repo first.
$running = Get-Process -Name ClipboardManager -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($PSScriptRoot, [System.StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    Step 'Stopping the running Clipboard Manager'
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Step "Building app ($Configuration)"
& dotnet build src/ClipboardManager.App -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# Determine version: parameter, GITHUB_REF_NAME (tag), or fallback to .csproj
if (-not $AppVersion -and $env:GITHUB_REF_NAME -match '^v?(\d+\.\d+\.\d+.*)$') {
    $AppVersion = $matches[1]
}
if ($AppVersion) {
    $version = $AppVersion.TrimStart('v').Trim()
} else {
    [xml]$proj = Get-Content src/ClipboardManager.App/ClipboardManager.App.csproj
    $version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}

if ($Publish) {
    Step "Publishing self-contained single-file exe v$version"
    & dotnet publish src/ClipboardManager.App -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$version -o publish
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
    Write-Host "`nDone: $(Join-Path $PSScriptRoot 'publish\ClipboardManager.exe')" -ForegroundColor Green
}

if ($Msix) {
    $appDir = Join-Path $PSScriptRoot 'publish\app'
    $dist = Join-Path $PSScriptRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null

    # Folder (not single-file) build: MSIX ships the unbundled app layout.
    Step "Publishing app folder for MSIX packaging v$version"
    if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
    & dotnet publish src/ClipboardManager.App -c Release -r win-x64 --self-contained true -p:DebugType=none -p:Version=$version -o $appDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
}

if ($Installer) {
    $installerSource = Join-Path $PSScriptRoot 'publish\installer'
    $dist = Join-Path $PSScriptRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null

    # Single-file build: avoids Smart App Control blocking loose unsigned runtime DLLs.
    Step "Publishing single-file executable for installer v$version"
    if (Test-Path $installerSource) { Remove-Item $installerSource -Recurse -Force }
    & dotnet publish src/ClipboardManager.App -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:Version=$version -o $installerSource
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

    Step "Building Setup.exe (Inno Setup) v$version"
    function Find-Iscc {
        $candidates = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
        )
        $hit = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($hit) { return $hit }
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
        return $null
    }
    $iscc = Find-Iscc
    if (-not $iscc) {
        $winget = Get-Command winget -ErrorAction SilentlyContinue
        if (-not $winget) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php, then run .\build.ps1 -Installer again." }
        Write-Host 'Inno Setup not found - installing with winget...' -ForegroundColor Yellow
        & winget install --id JRSoftware.InnoSetup --exact --scope user --accept-source-agreements --accept-package-agreements
        $iscc = Find-Iscc
        if (-not $iscc) { throw "Inno Setup installation did not complete. Install it from https://jrsoftware.org/isdl.php." }
    }
    & $iscc /Qp "/DAppVersion=$version" "/DSourceDir=$installerSource" "/DOutputDir=$dist" packaging\setup.iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }
    Write-Host "Done: $(Join-Path $dist "AdvancedClipboardManager-Setup-$version.exe")" -ForegroundColor Green
}

if ($Msix) {
    $numericVer = ($version -replace '-.*$', '')
    $msixVersion = "$numericVer.0"   # Store requires a 4-part version with revision 0
    Step "Building MSIX package v$msixVersion"

    # makeappx.exe: installed Windows SDK, otherwise the Microsoft.Windows.SDK.BuildTools NuGet package.
    function Find-MakeAppx($root) {
        Get-ChildItem $root -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    }
    $makeappx = Find-MakeAppx (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin')
    if (-not $makeappx) {
        $tools = Join-Path $PSScriptRoot '.tools\sdk-buildtools'
        $makeappx = Find-MakeAppx $tools
        if (-not $makeappx) {
            Write-Host 'Windows SDK not found - downloading Microsoft.Windows.SDK.BuildTools from nuget.org...'
            $feed = 'https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools'
            $ver = (Invoke-RestMethod "$feed/index.json").versions | Where-Object { $_ -notmatch '-' } | Select-Object -Last 1
            $nupkg = Join-Path $env:TEMP "sdk-buildtools.$ver.zip"
            Invoke-WebRequest "$feed/$ver/microsoft.windows.sdk.buildtools.$ver.nupkg" -OutFile $nupkg -UseBasicParsing
            Expand-Archive $nupkg -DestinationPath $tools -Force
            Remove-Item $nupkg -ErrorAction SilentlyContinue
            $makeappx = Find-MakeAppx $tools
            if (-not $makeappx) { throw "makeappx.exe not found in Microsoft.Windows.SDK.BuildTools $ver." }
        }
    }

    $layout = Join-Path $PSScriptRoot 'publish\msix'
    if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
    Copy-Item $appDir $layout -Recurse
    Copy-Item packaging\Assets (Join-Path $layout 'Assets') -Recurse
    Remove-Item (Join-Path $layout 'Assets\app.ico')

    $esc = { param($s) [System.Security.SecurityElement]::Escape($s) }
    (Get-Content packaging\AppxManifest.xml -Raw) `
        -replace '\$IdentityName\$', (& $esc $MsixIdentityName) `
        -replace '\$Publisher\$', (& $esc $MsixPublisher) `
        -replace '\$PublisherDisplayName\$', (& $esc $MsixPublisherDisplayName) `
        -replace '\$DisplayName\$', (& $esc $MsixDisplayName) `
        -replace '\$Version\$', $msixVersion |
        Set-Content (Join-Path $layout 'AppxManifest.xml') -Encoding utf8

    $msixFile = Join-Path $dist "AdvancedClipboardManager_${msixVersion}_x64.msix"
    & $makeappx.FullName pack /d $layout /p $msixFile /o | Where-Object { $_ -notmatch 'as a payload file|^\s*$' }
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed." }
    Write-Host "Done: $msixFile" -ForegroundColor Green
    Write-Host 'Upload it in Partner Center (the Store signs it). To try it locally first (Developer Mode on):'
    Write-Host "  Add-AppxPackage -Register `"$(Join-Path $layout 'AppxManifest.xml')`""
}

if ($Run) {
    Step 'Starting Clipboard Manager (tray icon, Ctrl+Shift+V)'
    $exe = Get-ChildItem "src/ClipboardManager.App/bin/$Configuration" -Recurse -Filter ClipboardManager.exe | Select-Object -First 1
    if (-not $exe) { throw "ClipboardManager.exe not found after build." }
    Start-Process $exe.FullName
}

Write-Host "`nOK" -ForegroundColor Green
