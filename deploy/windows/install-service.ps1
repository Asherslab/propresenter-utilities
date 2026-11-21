<#
.SYNOPSIS
    Registers ProPresenter Utilities as a Windows service.

.DESCRIPTION
    The application host already calls UseWindowsService(), so the published executable can be
    registered directly with the service control manager. Run this from an elevated PowerShell.

    The service runs as LocalSystem by default, whose profile directory is not yours, so the config
    path is always passed explicitly rather than left to the per-user default.

.EXAMPLE
    .\install-service.ps1 -InstallPath "C:\Program Files\ProPresenter Utilities"

.EXAMPLE
    .\install-service.ps1 -InstallPath "C:\Program Files\ProPresenter Utilities" -Urls "http://0.0.0.0:8752"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallPath,

    [string]$ConfigPath = "C:\ProgramData\propresenter-utilities\config.json",

    # Loopback only by default: neither this UI nor the ProPresenter API is authenticated.
    [string]$Urls = "http://127.0.0.1:8752",

    [string]$ServiceName = "ProPresenterUtilities"
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run from an elevated PowerShell session."
}

$exe = Join-Path $InstallPath "propresenter-utilities.exe"
if (-not (Test-Path $exe)) {
    throw "Could not find $exe. Publish the app to $InstallPath first."
}

# Create the config directory up front so the service can write to it on first run.
$configDir = Split-Path -Parent $ConfigPath
if (-not (Test-Path $configDir)) {
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    Write-Host "Created $configDir"
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Service '$ServiceName' already exists; stopping and removing it."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

$binaryPath = '"{0}" --config "{1}" --urls "{2}"' -f $exe, $ConfigPath, $Urls

New-Service -Name $ServiceName `
    -DisplayName "ProPresenter Utilities" `
    -Description "Fires ProPresenter cues when its timers reach configured points." `
    -BinaryPathName $binaryPath `
    -StartupType Automatic | Out-Null

# Restart on failure rather than leaving a service without its cue automation.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/15000 | Out-Null

Start-Service -Name $ServiceName
Get-Service -Name $ServiceName

Write-Host ""
Write-Host "Installed. Control panel: $Urls"
Write-Host "Config file:  $ConfigPath"
Write-Host "Logs:         Get-EventLog -LogName Application -Source 'ProPresenter Utilities' -Newest 20"
Write-Host ""
Write-Host "To remove:    Stop-Service $ServiceName; sc.exe delete $ServiceName"
