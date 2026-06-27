<#
  Builds the KerwaKasse installer locally.

  Steps:
    1. read <Version> from KerwaKasse\KerwaKasse.csproj
    2. dotnet publish (self-contained, win-x64)
    3. compile installer\KerwaKasse.iss with Inno Setup
         -> installer\Output\KerwaKasse_Setup_vX.Y.Z.exe

  Prerequisite: Inno Setup 6 installed   (winget install JRSoftware.InnoSetup)
  Usage:        run from the repo root:  .\publish.ps1
#>
$ErrorActionPreference = "Stop"
$root       = $PSScriptRoot
$csprojPath = Join-Path $root "KerwaKasse\KerwaKasse.csproj"
$issPath    = Join-Path $root "installer\KerwaKasse.iss"

# 1. Version from the .csproj (<Version>X.Y.Z</Version>) -> single source of truth.
[xml]$csproj = Get-Content $csprojPath
$version = @($csproj.Project.PropertyGroup) |
    ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $csprojPath" }
$version = "$version".Trim()
Write-Host "==> Building KerwaKasse $version" -ForegroundColor Cyan

# 2. Publish a self-contained win-x64 build.
dotnet publish $csprojPath -c Release -r win-x64 --self-contained true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 3. Locate the Inno Setup compiler (ISCC.exe). winget installs Inno per-user under
#    %LOCALAPPDATA%, so check the registry install location and the common folders too.
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $folders = @("${env:LOCALAPPDATA}\Programs\Inno Setup 6",
                 "${env:ProgramFiles(x86)}\Inno Setup 6",
                 "${env:ProgramFiles}\Inno Setup 6")
    foreach ($key in @(
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1")) {
        $loc = (Get-ItemProperty -Path $key -ErrorAction SilentlyContinue).InstallLocation
        if ($loc) { $folders = @($loc) + $folders }
    }
    foreach ($f in $folders) {
        $candidate = Join-Path $f "ISCC.exe"
        if (Test-Path $candidate) { $iscc = $candidate; break }
    }
}
if (-not $iscc) { throw "ISCC.exe not found. Install Inno Setup:  winget install JRSoftware.InnoSetup" }
Write-Host "    using $iscc"

# 4. Compile the installer (version handed to the .iss via a define).
& $iscc "/DAppVersion=$version" $issPath
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

Write-Host "==> Done: installer\Output\KerwaKasse_Setup_v$version.exe" -ForegroundColor Green
