<#
.SYNOPSIS
    Publishes Ppinpix as a self-contained win-x64 release and builds the Windows installer (.exe).
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "src\Ppinpix.App\Ppinpix.App.csproj"
$publishDir = Join-Path $repoRoot "dist\win-x64"
$issPath = Join-Path $repoRoot "installer\Ppinpix.iss"

# Locate dotnet
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $dotnet -or -not (& $dotnet --list-sdks 2>$null | Select-String "^10\.")) {
    $userDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path $userDotnet) {
        $dotnet = $userDotnet
    }
}
if (-not $dotnet) {
    throw "Could not find .NET 10 SDK."
}

# Locate Inno Setup compiler
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Could not find Inno Setup 6 (ISCC.exe). Install via: winget install JRSoftware.InnoSetup"
}

Write-Host "==> Cleaning previous publish output..." -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

Write-Host "==> Publishing self-contained win-x64 release..." -ForegroundColor Cyan
& $dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "==> Compiling Inno Setup installer..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$installerExe = Join-Path $repoRoot "dist\installer\Ppinpix-Setup-$Version-win-x64.exe"
if (Test-Path $installerExe) {
    $sizeMb = [math]::Round((Get-Item $installerExe).Length / 1MB, 2)
    Write-Host "==> Installer ready: $installerExe ($sizeMb MB)" -ForegroundColor Green
}
