<#
.SYNOPSIS  Stops and removes the LIMS Instrument Import Windows Service.
.EXAMPLE   .\Uninstall-LimsWindowsService.ps1
#>
#Requires -RunAsAdministrator
$ServiceName = "LimsInstrumentImport"
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Stop-Service  -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Remove-Service -Name $ServiceName
    Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
} else {
    Write-Host "Service '$ServiceName' not found." -ForegroundColor DarkYellow
}
if ([System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
    [System.Diagnostics.EventLog]::DeleteEventSource($ServiceName)
    Write-Host "Event Log source removed." -ForegroundColor Green
}
