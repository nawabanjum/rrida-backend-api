<#
.SYNOPSIS
    Restores a CloverEra production dump into the RRIDA dev database and
    baselines the EF migration history.

.DESCRIPTION
    The dump is pg_dump custom format and carries no CREATE DATABASE, so it
    restores into any target name. Tables land in the cloverera_admin schema,
    which is what the EF model expects to find via search_path.

    Steps: open a firewall rule for this machine, restore, baseline migration
    history, verify, close the firewall rule.

.EXAMPLE
    $env:PGPASSWORD = '<postgres admin password>'
    ./restore-dev.ps1
#>
[CmdletBinding()]
param(
    [string]$DumpFile = 'D:\DB_Backups\CloverEra_prod_20260819_111905.dump',
    [string]$ResourceGroup = 'rg-rrida-dev-001',
    [string]$ServerName = 'pg-rrida-dev-001',
    [string]$DatabaseName = 'rrida',
    [string]$AdminUser = 'rridaadmin',
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepFirewallRule,
    [switch]$SkipScrub
)

$ErrorActionPreference = 'Stop'

if (-not $env:PGPASSWORD) {
    throw "PGPASSWORD is not set. Set it to the Postgres admin password first."
}
if (-not (Test-Path $DumpFile)) {
    throw "Dump not found: $DumpFile"
}

$pgRestore = Join-Path $PgBin 'pg_restore.exe'
$psql = Join-Path $PgBin 'psql.exe'
foreach ($tool in @($pgRestore, $psql)) {
    if (-not (Test-Path $tool)) { throw "Not found: $tool" }
}

$serverHost = "$ServerName.postgres.database.azure.com"
$ruleName = "restore-$(Get-Random -Maximum 99999)"
$env:PGSSLMODE = 'require'

$myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org?format=json').ip
Write-Host "This machine: $myIp" -ForegroundColor Cyan

Write-Host "Opening firewall rule $ruleName ..." -ForegroundColor Cyan
az postgres flexible-server firewall-rule create `
    --resource-group $ResourceGroup `
    --name $ServerName `
    --rule-name $ruleName `
    --start-ip-address $myIp `
    --end-ip-address $myIp `
    --output none

try {
    # pgvector must exist before the DocumentEmbeddings tables restore.
    Write-Host "Ensuring vector extension ..." -ForegroundColor Cyan
    & $psql "host=$serverHost port=5432 dbname=$DatabaseName user=$AdminUser sslmode=require" `
        -v ON_ERROR_STOP=1 -c "CREATE EXTENSION IF NOT EXISTS vector;"

    # --no-owner / --no-privileges: the cloverera_admin role does not exist on
    # this server, so ownership and GRANT statements would fail.
    Write-Host "Restoring $DumpFile into $DatabaseName ..." -ForegroundColor Cyan
    & $pgRestore `
        -h $serverHost -p 5432 -U $AdminUser -d $DatabaseName `
        --no-owner --no-privileges `
        -j 4 `
        $DumpFile

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "pg_restore exited $LASTEXITCODE. Errors about missing roles or extensions are expected; anything else needs a look."
    }

    Write-Host "Baselining EF migration history ..." -ForegroundColor Cyan
    & $psql "host=$serverHost port=5432 dbname=$DatabaseName user=$AdminUser sslmode=require" `
        -v ON_ERROR_STOP=1 -f (Join-Path $PSScriptRoot 'baseline-migrations.sql')

    if ($SkipScrub) {
        Write-Warning "Skipping the PII scrub. This database now holds real production personal data."
    }
    else {
        Write-Host "Scrubbing personal data ..." -ForegroundColor Cyan
        & $psql "host=$serverHost port=5432 dbname=$DatabaseName user=$AdminUser sslmode=require" `
            -v ON_ERROR_STOP=1 -f (Join-Path $PSScriptRoot 'scrub-pii.sql')
    }

    Write-Host "Verifying ..." -ForegroundColor Cyan
    & $psql "host=$serverHost port=5432 dbname=$DatabaseName user=$AdminUser sslmode=require" -c @"
SELECT
    (SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public') AS public_tables,
    (SELECT count(*) FROM information_schema.tables WHERE table_schema = 'export') AS export_tables,
    (SELECT count(*) FROM public."__EFMigrationsHistory")                          AS migrations_baselined,
    pg_size_pretty(pg_database_size(current_database()))                           AS size;
"@
}
finally {
    if ($KeepFirewallRule) {
        Write-Host "Leaving firewall rule $ruleName in place." -ForegroundColor Yellow
    }
    else {
        Write-Host "Removing firewall rule $ruleName ..." -ForegroundColor Cyan
        az postgres flexible-server firewall-rule delete `
            --resource-group $ResourceGroup `
            --name $ServerName `
            --rule-name $ruleName `
            --yes --output none
    }
}

Write-Host ""
Write-Host "Done. Connection string for the RRIDA-DEV Key Vault secret:" -ForegroundColor Green
Write-Host "Host=$serverHost;Database=$DatabaseName;Username=$AdminUser;Password=<pw>;SSL Mode=Require;Trust Server Certificate=true"
