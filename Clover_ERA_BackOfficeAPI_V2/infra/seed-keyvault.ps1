<#
.SYNOPSIS
    Seeds the RRIDA dev Key Vault: copies the shared application secrets from
    the existing Clovera vault and writes a fresh RRIDA connection string.

.DESCRIPTION
    Secret VALUES are never printed, never written to disk and never passed on a
    command line that ends up in shell history - each one is read into memory
    from the source vault and written straight to the target vault.

    Connection strings are deliberately NOT copied. Clovera's Dev/UAT/LIVE
    entries point at Clovera databases, and RRIDA must never reach them. The
    RRIDA-DEV secret is built here from the local password file instead.

.EXAMPLE
    ./seed-keyvault.ps1
    ./seed-keyvault.ps1 -WhatIf     # show what would be copied
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceVault = 'clovera-secret-keys',
    [string]$TargetVault = 'kv-rrida-dev-001',
    [string]$PostgresServerName = 'pg-rrida-dev-001',
    [string]$DatabaseName = 'rrida',
    [string]$AdminUser = 'rridaadmin',
    [string]$PasswordFile = 'D:\Secrets\RRIDA\pg-rrida-dev-001.admin.txt',
    [string]$ConnectionSecretName = 'RRIDA-DEV'
)

$ErrorActionPreference = 'Stop'

# Names come from AzureKeyVault:SecretNames and StripeSecretNames:Dev in
# appsettings.json. JwtSettingsKry is spelled that way in the config and in the
# vault; Program.cs line 111 dereferences it with no null guard, so the app
# crash-loops without it.
$secretsToCopy = @(
    'JwtSettingsKry'
    'EncryptionIV'
    'EncryptionKey'
    'SendGrid'
    'ResendApiKey'
    'AzureRagSearch'
    'ApiSecretKey'
    'AzureMarketplaceClientSecret'
    'OpenAI'
    'Dev-StripeSecretKey'
    'Dev-StripePublishableKey'
    'Dev-StripeWebhookSecret'
)

Write-Host "Source: $SourceVault" -ForegroundColor Cyan
Write-Host "Target: $TargetVault" -ForegroundColor Cyan
Write-Host ""

$copied = 0
$missing = @()

foreach ($name in $secretsToCopy) {
    $value = az keyvault secret show --vault-name $SourceVault --name $name --query value -o tsv 2>$null

    if ([string]::IsNullOrWhiteSpace($value)) {
        Write-Warning "$name not found in $SourceVault - skipped."
        $missing += $name
        continue
    }

    if ($PSCmdlet.ShouldProcess("$TargetVault/$name", 'Set secret')) {
        az keyvault secret set --vault-name $TargetVault --name $name --value $value --output none
        Write-Host "  copied $name" -ForegroundColor Green
        $copied++
    }
    else {
        Write-Host "  would copy $name"
    }

    Remove-Variable value
}

# The connection string is built here rather than copied: nothing in the source
# vault points at the RRIDA database.
if (-not (Test-Path $PasswordFile)) {
    throw "Password file not found: $PasswordFile"
}

$pw = (Get-Content $PasswordFile -Raw).Trim()
$serverHost = "$PostgresServerName.postgres.database.azure.com"
$connectionString = "Host=$serverHost;Database=$DatabaseName;Username=$AdminUser;Password=$pw;SSL Mode=Require;Trust Server Certificate=true"

if ($PSCmdlet.ShouldProcess("$TargetVault/$ConnectionSecretName", 'Set connection string')) {
    az keyvault secret set --vault-name $TargetVault --name $ConnectionSecretName --value $connectionString --output none
    Write-Host "  wrote $ConnectionSecretName" -ForegroundColor Green
    $copied++
}

Remove-Variable pw, connectionString

Write-Host ""
Write-Host "$copied secrets in $TargetVault." -ForegroundColor Green
if ($missing.Count) {
    Write-Warning "Missing from source: $($missing -join ', ')"
}
Write-Host "Verify names only (values stay hidden):" -ForegroundColor Cyan
az keyvault secret list --vault-name $TargetVault --query "[].name" -o tsv | Sort-Object
