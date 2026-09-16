<#
.SYNOPSIS
    Builds (optionally) and deploys SQL Companion into SSMS 20, then merges its menus.

.DESCRIPTION
    SSMS 20 is the Visual Studio 2017 (15.0) ISOLATED SHELL app "ssms". Two facts drive this script:

    1. Its standalone VSIXInstaller.exe CANNOT install this extension (NoApplicableSKUsException) --
       it resolves install targets through the modern VS setup catalog, which never lists the
       isolated shell. So we install by COPYING the VSIX payload into an Extensions folder.

    2. Copying into the PER-USER Extensions folder makes SSMS *load* the extension, but it does NOT
       merge the command table -- the Tools-menu items / toolbar never appear. To get the menus,
       the extension must live in the MACHINE-WIDE Extensions folder
       (<SSMS>\Common7\IDE\Extensions\SQLCompanion) AND you must run "ssms.exe /setup" once to
       rebuild the merged menu resources. (Verified: after machine-wide deploy + /setup, "Column
       Search" and "Relationships" appear under the Tools menu.)

    This script therefore: builds (unless -NoBuild), copies extension.vsixmanifest + SQLCompanion.dll
    + SQLCompanion.pkgdef into the machine Extensions folder, clears the per-user extension cache,
    and runs "ssms.exe /setup". Start SSMS afterwards.

    >>> RUN THIS FROM AN ELEVATED (Administrator) PowerShell <<< -- it writes under Program Files and
    runs /setup. The script relaunches itself elevated if needed.

.PARAMETER Configuration   Debug (default) or Release.
.PARAMETER NoBuild         Skip MSBuild; deploy the existing bin\<Configuration>\SQLCompanion.vsix.
.PARAMETER MSBuild         Path to MSBuild.exe (defaults to VS2022 Enterprise).
.PARAMETER SsmsRoot        SSMS install root. Auto-detected from the registry if omitted.
.PARAMETER Uninstall       Remove the deployed extension and re-run /setup.

.EXAMPLE  .\deploy.ps1
.EXAMPLE  .\deploy.ps1 -Configuration Release -NoBuild
.EXAMPLE  .\deploy.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [string]$MSBuild = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
    [string]$SsmsRoot,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok($m)   { Write-Host "    $m" -ForegroundColor Green }
function Write-Warn2($m){ Write-Host "    $m" -ForegroundColor Yellow }

# --- Re-launch elevated if we're not admin (needed to write Program Files + run /setup) ----------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warn2 'Not elevated - relaunching as Administrator...'
    $argList = @('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$PSCommandPath`"",
                 '-Configuration',$Configuration,'-MSBuild',"`"$MSBuild`"")
    if ($NoBuild)   { $argList += '-NoBuild' }
    if ($Uninstall) { $argList += '-Uninstall' }
    if ($SsmsRoot)  { $argList += @('-SsmsRoot',"`"$SsmsRoot`"") }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList
    return
}

# --- Resolve paths ------------------------------------------------------------------------------
$RepoRoot   = $PSScriptRoot
$Solution   = Join-Path $RepoRoot 'SQLCompanion.sln'
$ProjectDir = Join-Path $RepoRoot 'SQLCompanion'
$Vsix       = Join-Path $ProjectDir "bin\$Configuration\SQLCompanion.vsix"

if (-not $SsmsRoot) {
    $reg = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Microsoft SQL Server Management Studio\20'
    if (Test-Path $reg) { $SsmsRoot = (Get-ItemProperty $reg).SSMSInstallRoot }
    if (-not $SsmsRoot) { $SsmsRoot = 'C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\' }
}
$SsmsExe   = Join-Path $SsmsRoot 'Common7\IDE\ssms.exe'
$DeployDir = Join-Path $SsmsRoot 'Common7\IDE\Extensions\SQLCompanion'
$UserCache = Join-Path $env:LOCALAPPDATA 'Microsoft\SQL Server Management Studio\20.0_IsoShell\Extensions'

if (-not (Test-Path $SsmsExe)) { throw "ssms.exe not found at '$SsmsExe'. Pass -SsmsRoot <path>." }

# --- SSMS must be closed (locked DLL, and /setup needs exclusive access) -------------------------
if (Get-Process -Name Ssms -ErrorAction SilentlyContinue) {
    Write-Warn2 'Closing running SSMS (required to deploy + run /setup)...'
    Stop-Process -Name Ssms -Force
    Start-Sleep -Seconds 3
}

function Invoke-Setup {
    Write-Step 'Merging menus: ssms.exe /setup ...'
    $p = Start-Process -FilePath $SsmsExe -ArgumentList '/setup' -Wait -PassThru
    if ($p.ExitCode -ne 0) { Write-Warn2 "ssms.exe /setup returned $($p.ExitCode)." } else { Write-Ok 'Menu merge complete.' }
}

function Clear-UserCache {
    if (Test-Path $UserCache) {
        Get-ChildItem -LiteralPath $UserCache -Filter '*.cache' -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force; Write-Ok "cleared cache: $($_.Name)" }
    }
}

# --- Uninstall ----------------------------------------------------------------------------------
if ($Uninstall) {
    Write-Step "Uninstalling: $DeployDir"
    if (Test-Path -LiteralPath $DeployDir) { Remove-Item -LiteralPath $DeployDir -Recurse -Force; Write-Ok 'Removed.' }
    else { Write-Warn2 'Not found (nothing to remove).' }
    Clear-UserCache
    Invoke-Setup
    Write-Step 'Done. Start SSMS.'
    return
}

# --- Build --------------------------------------------------------------------------------------
if (-not $NoBuild) {
    if (-not (Test-Path -LiteralPath $MSBuild)) { throw "MSBuild not found at '$MSBuild'. Pass -MSBuild <path>." }
    Write-Step "Building $Configuration (DeployExtension=false)..."
    & $MSBuild $Solution /p:Configuration=$Configuration /p:DeployExtension=false /t:Rebuild /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
    Write-Ok 'Build succeeded.'
}
if (-not (Test-Path -LiteralPath $Vsix)) { throw "VSIX not found at '$Vsix'. Build first, or drop -NoBuild." }

# --- Deploy machine-wide (extract the vsix payload, skipping [Content_Types].xml) ----------------
Write-Step "Deploying to: $DeployDir"
if (Test-Path -LiteralPath $DeployDir) { Remove-Item -LiteralPath $DeployDir -Recurse -Force }
New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($Vsix)
try {
    foreach ($e in $zip.Entries) {
        if ($e.FullName -eq '[Content_Types].xml') { continue }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $DeployDir $e.FullName), $true)
    }
} finally { $zip.Dispose() }
Write-Ok 'Deployed files:'
Get-ChildItem -LiteralPath $DeployDir | ForEach-Object { Write-Host "      $($_.Name)" }

Clear-UserCache
Invoke-Setup

Write-Step 'Done. Start SSMS 20 -- the "SQL Companion" submenu appears under the Tools menu.'
