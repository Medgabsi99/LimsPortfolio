# ============================================================================
# LIMS - Database setup script
# Runs all SQL scripts against a SQL Server / LocalDB instance.
# Handles GO batch separators (which ADO.NET does not understand).
#
# Usage:
#   .\run-all.ps1                                          # default LocalDB
#   .\run-all.ps1 -ServerInstance "(localdb)\MSSQLLocalDB"
#   .\run-all.ps1 -ServerInstance "localhost" -SqlAuthUser sa `
#                 -SqlAuthPassword (Read-Host -AsSecureString "sa password")
# ============================================================================
param(
    [string]$ServerInstance = "(localdb)\MSSQLLocalDB",
    [string]$Database = "master",
    [string]$SqlAuthUser,
    [SecureString]$SqlAuthPassword,
    [string]$ScriptsPath = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Data

# ---- Build connection -------------------------------------------------------
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$builder["Server"]   = $ServerInstance
$builder["Database"] = $Database
$builder["TrustServerCertificate"] = $true
if ($SqlAuthUser) {
    # Convert the SecureString to plain text only at the ADO.NET boundary
    $plainPassword = [System.Net.NetworkCredential]::new("", $SqlAuthPassword).Password
    $builder["User ID"]     = $SqlAuthUser
    $builder["Password"]    = $plainPassword
} else {
    $builder["Integrated Security"] = $true
}

$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = $builder.ConnectionString
$conn.Open()
Write-Host "Connected to $ServerInstance" -ForegroundColor Green

function Invoke-Batch([string]$batch) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $batch
    $cmd.CommandTimeout = 120
    try { $cmd.ExecuteNonQuery() | Out-Null }
    finally { $cmd.Dispose() }
}

function Invoke-SqlFile([string]$path) {
    Write-Host "Running $(Split-Path $path -Leaf)..." -NoNewline
    $content = [System.IO.File]::ReadAllText($path)

    # Split on GO batch separators (own line, case-insensitive)
    $batches = $content -split "(?im)^\s*GO\s*$"
    foreach ($batch in $batches) {
        if ($batch.Trim().Length -gt 0) { Invoke-Batch $batch }
    }
    Write-Host " OK" -ForegroundColor Green
}

# ---- Run scripts in dependency order ----------------------------------------
$scripts = @(
    "01_tables.sql",
    "02_views.sql",
    "03_stored_procedures.sql",
    "04_seed_data.sql"
)

foreach ($script in $scripts) {
    Invoke-SqlFile (Join-Path $ScriptsPath $script)
}

# ---- Sanity check ------------------------------------------------------------
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT COUNT(1) FROM LimsDb.dbo.Samples;"
$samples = $cmd.ExecuteScalar()
$cmd.CommandText = "SELECT COUNT(1) FROM LimsDb.dbo.Results;"
$results = $cmd.ExecuteScalar()
$conn.Close()

Write-Host ""
Write-Host "Database ready: $samples samples, $results results in LimsDb." -ForegroundColor Green
Write-Host "Connection string for appsettings.json:"
Write-Host "  `"LimsDb`": `"Server=$ServerInstance;Database=LimsDb;Trusted_Connection=True;TrustServerCertificate=True;`""