<#
.SYNOPSIS
  One command: create the database, install pgvector if it is available, start the API, seed a
  realistic call-center archive, and show the suggestion engine answering new tickets from it.

.DESCRIPTION
  The PostgreSQL password is read from the PGPASSWORD environment variable and is never written to
  disk or echoed. Set it in your own shell immediately before running:

      $env:PGPASSWORD = '<your postgres password>'
      ./tools/demo.ps1

  Everything else has a sensible default.

.PARAMETER PgUser      PostgreSQL superuser (default: postgres)
.PARAMETER PgHost      Host (default: localhost)
.PARAMETER PgPort      Port (default: 5432)
.PARAMETER Database    Database to create and use (default: resolvedesk)
.PARAMETER Port        Port the API listens on (default: 8080)
.PARAMETER SkipSeed    Start the API only; do not seed or run the demo.
#>
[CmdletBinding()]
param(
  [string]$PgUser = "postgres",
  [string]$PgHost = "localhost",
  [int]$PgPort = 5432,
  [string]$Database = "resolvedesk",
  [int]$Port = 8080,
  [switch]$SkipSeed
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not $env:PGPASSWORD) {
  Write-Error "PGPASSWORD is not set. Run:  `$env:PGPASSWORD = '<your postgres password>'  then re-run this script."
}

# psql ships with the server install but is not always on PATH.
$psql = (Get-Command psql -EA SilentlyContinue)?.Source
if (-not $psql) {
  $psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" -EA SilentlyContinue |
          Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $psql) { Write-Error "psql not found. Add PostgreSQL's bin directory to PATH." }

Write-Host "`n[1/5] Database" -ForegroundColor Cyan

$exists = & $psql -h $PgHost -p $PgPort -U $PgUser -d postgres -tAc `
  "SELECT 1 FROM pg_database WHERE datname = '$Database'"
if ($LASTEXITCODE -ne 0) { Write-Error "Could not connect to PostgreSQL. Check PGPASSWORD, host and port." }

if ($exists -ne "1") {
  & $psql -h $PgHost -p $PgPort -U $PgUser -d postgres -c "CREATE DATABASE $Database" | Out-Null
  Write-Host "      created database '$Database'" -ForegroundColor Green
} else {
  Write-Host "      database '$Database' already exists"
}

# pgvector is optional: without it the product falls back to keyword search rather than failing.
& $psql -h $PgHost -p $PgPort -U $PgUser -d $Database -c "CREATE EXTENSION IF NOT EXISTS vector" 2>&1 | Out-Null
$hasVector = (& $psql -h $PgHost -p $PgPort -U $PgUser -d $Database -tAc `
  "SELECT count(*) FROM pg_extension WHERE extname = 'vector'") -eq "1"
Write-Host ("      pgvector: " + $(if ($hasVector) { "installed" } else { "not available — keyword search only" })) `
  -ForegroundColor $(if ($hasVector) { "Green" } else { "Yellow" })

Write-Host "`n[2/5] Build" -ForegroundColor Cyan
dotnet build "$root\ResolveDesk.slnx" -v q --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed." }

Write-Host "`n[3/5] API" -ForegroundColor Cyan
$connectionString = "Host=$PgHost;Port=$PgPort;Database=$Database;Username=$PgUser;Password=$env:PGPASSWORD"
$job = Start-Job -ScriptBlock {
  param($root, $port, $cs)
  Set-Location $root
  $env:ASPNETCORE_URLS = "http://localhost:$port"
  $env:ASPNETCORE_ENVIRONMENT = "Development"
  $env:DB_CONNECTION_STRING = $cs
  dotnet run --project src/ResolveDesk.WebApi --no-launch-profile --no-build 2>&1
} -ArgumentList $root, $Port, $connectionString

try {
  $ready = $false
  foreach ($attempt in 1..40) {
    Start-Sleep -Milliseconds 750
    try {
      if ((Invoke-WebRequest "http://localhost:$Port/health/live" -TimeoutSec 3 -UseBasicParsing).StatusCode -eq 200) {
        $ready = $true; break
      }
    } catch { }
  }
  if (-not $ready) {
    Receive-Job $job | Select-Object -Last 25
    Write-Error "API did not become healthy on port $Port."
  }
  Write-Host "      healthy on http://localhost:$Port" -ForegroundColor Green

  if ($SkipSeed) {
    Write-Host "`nAPI is running. Press Ctrl+C to stop." -ForegroundColor Cyan
    Wait-Job $job | Out-Null
    return
  }

  Write-Host "`n[4/5] Seed + demo" -ForegroundColor Cyan
  $env:RESOLVEDESK_API_URL = "http://localhost:$Port"
  node "$root\tools\seed-demo.mjs"

  Write-Host "`n[5/5] Done" -ForegroundColor Cyan
  Write-Host "      API      http://localhost:$Port"
  Write-Host "      OpenAPI  http://localhost:$Port/openapi/v1.json"
  Write-Host "      Web      cd frontend; npm run dev   →  http://localhost:5173"
}
finally {
  Stop-Job $job -EA SilentlyContinue
  Remove-Job $job -Force -EA SilentlyContinue
}
