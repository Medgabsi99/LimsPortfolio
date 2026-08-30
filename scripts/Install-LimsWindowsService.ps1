#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $InstallPath    = "C:\Lims\Services\InstrumentImport",
    [string] $ServiceAccount = "NT AUTHORITY\LOCAL SERVICE",
    [string] $SoapApiKey     = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ServiceName = "LimsInstrumentImport"
$DisplayName = "LIMS Instrument Import Service"
$Description = "Polls the instrument incoming folder, parses CSV results and writes them to the LIMS database."
$ExePath     = Join-Path $InstallPath "Lims.WindowsService.exe"

Write-Host "`n=== LIMS Windows Service Installer ===" -ForegroundColor Cyan

# 1. Event Log source
Write-Host "`n[1/5] Registering Event Log source '$ServiceName'..." -ForegroundColor Yellow
if ([System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
    Write-Host "      Already exists - skipped." -ForegroundColor DarkGray
} else {
    [System.Diagnostics.EventLog]::CreateEventSource($ServiceName, "Application")
    Write-Host "      Event Log source created." -ForegroundColor Green
}

# 2. Instrument data folders
Write-Host "`n[2/5] Creating instrument data folders..." -ForegroundColor Yellow
"C:\Lims\InstrumentData\incoming", "C:\Lims\InstrumentData\archive", "C:\Lims\InstrumentData\error" | ForEach-Object {
    New-Item -ItemType Directory -Force $_ | Out-Null
    Write-Host "      $_" -ForegroundColor Green
}

# 3. Install or update service
Write-Host "`n[3/5] Installing service '$ServiceName'..." -ForegroundColor Yellow
if (-not (Test-Path $ExePath)) {
    throw "Executable not found at '$ExePath'. Publish the WindowsService project first."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "      Service exists - updating binary path..." -ForegroundColor DarkGray
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe config $ServiceName binPath= "`"$ExePath`"" | Out-Null
} else {
    New-Service -Name $ServiceName -DisplayName $DisplayName -Description $Description `
        -BinaryPathName "`"$ExePath`"" -StartupType Automatic | Out-Null
    sc.exe config $ServiceName obj= "$ServiceAccount" | Out-Null
}
Write-Host "      Done." -ForegroundColor Green

# 4. Environment variables
Write-Host "`n[4/5] Setting environment variables..." -ForegroundColor Yellow
if ($SoapApiKey) {
    [System.Environment]::SetEnvironmentVariable("SoapService__ApiKey", $SoapApiKey, "Machine")
    Write-Host "      SoapService__ApiKey set (Machine scope)." -ForegroundColor Green
} else {
    Write-Host "      SoapApiKey not provided - skipped." -ForegroundColor DarkYellow
}
Write-Host "      REMINDER: also set Jwt__SigningKey (Machine) for the REST API." -ForegroundColor DarkYellow

# 5. Start service
Write-Host "`n[5/5] Starting service..." -ForegroundColor Yellow
Start-Service -Name $ServiceName
$status = (Get-Service -Name $ServiceName).Status
if ($status -eq "Running") {
    Write-Host "      Status: $status" -ForegroundColor Green
} else {
    Write-Host "      Status: $status" -ForegroundColor Red
}

Write-Host "`n=== Installation complete ===" -ForegroundColor Cyan
Write-Host "Service : $ServiceName ($status)"
Write-Host "Folders : C:\Lims\InstrumentData\{incoming,archive,error}"
