<#
.SYNOPSIS
    Seeds an RRIDA Key Vault with the shared application secrets held in the
    API project's local user-secrets. Used once per new environment.

.DESCRIPTION
    A service connection scoped to one resource group cannot read another
    environment's vault, so the Pre-Prod pipeline can write the connection
    string but not copy the shared secrets. Those values already exist in the
    developer's user-secrets store, and this copies them across.

    Secret VALUES are never printed. Each is read into memory and passed
    straight to az, then read back and compared so a mangled value is caught
    here rather than by a crashing app later.

    Existing secrets are left alone unless -Overwrite is given, so a value set
    by hand in the target vault is never replaced by accident.

    Stripe keys are renamed for the target environment: the local Dev-Stripe*
    entries become PreProd-Stripe* (Stripe test keys are fine in Pre-Prod; set
    real ones by hand if that changes).

.EXAMPLE
    az login
    ./seed-keyvault-from-user-secrets.ps1
    ./seed-keyvault-from-user-secrets.ps1 -Overwrite  # replace existing values
    ./seed-keyvault-from-user-secrets.ps1 -WhatIf     # show what would be set
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$TargetVault = 'kv-rrida-preprod-001',
    [string]$Project = "$PSScriptRoot/../CloverBackoffice/CloverBackoffice.API",
    [string]$StripePrefix = 'PreProd',
    [switch]$GrantSelfAccess,
    [switch]$Overwrite
)

$ErrorActionPreference = 'Stop'

# Names the API reads (AzureKeyVault:SecretNames). The connection string is NOT
# here: the pipeline writes RRIDA-PREPROD itself from the deployed server.
$shared = @(
    'JwtSettingsKry'
    'EncryptionIV'
    'EncryptionKey'
    'SendGrid'
    'ResendApiKey'
    'AzureRagSearch'
    'ApiSecretKey'
    'AzureMarketplaceClientSecret'
    'OpenAI'
)
$stripe = @{
    'Dev-StripeSecretKey'      = "$StripePrefix-StripeSecretKey"
    'Dev-StripePublishableKey' = "$StripePrefix-StripePublishableKey"
    'Dev-StripeWebhookSecret'  = "$StripePrefix-StripeWebhookSecret"
}

Write-Host "Target vault: $TargetVault" -ForegroundColor Cyan

if ($GrantSelfAccess) {
    # Works with Contributor on the resource group: an access policy is a
    # management-plane change, unlike an RBAC role assignment.
    $me = az ad signed-in-user show --query id -o tsv
    if (-not $me) { throw "Could not resolve the signed-in user. Run 'az login' first." }
    Write-Host "Granting yourself get/list/set on $TargetVault ..." -ForegroundColor Cyan
    az keyvault set-policy --name $TargetVault --object-id $me --secret-permissions get list set --output none
}

# Read user-secrets once. --json keeps values out of the console. The CLI
# returns one line per array element, so join before parsing, and it wraps the
# JSON in //BEGIN / //END marker lines.
$secretsJson = (dotnet user-secrets list --project $Project --json) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) { throw "Could not read user-secrets from $Project." }

$start = $secretsJson.IndexOf('{')
$end = $secretsJson.LastIndexOf('}')
if ($start -lt 0 -or $end -le $start) { throw "Unexpected user-secrets output from $Project." }
$secrets = ($secretsJson.Substring($start, $end - $start + 1) | ConvertFrom-Json)

$existing = @(az keyvault secret list --vault-name $TargetVault --query "[].name" -o tsv)
if ($LASTEXITCODE -ne 0) { throw "Cannot list secrets in $TargetVault. Re-run with -GrantSelfAccess." }

$set = 0; $skipped = 0; $missing = @()

function Copy-Secret {
    param([string]$From, [string]$To)

    $value = $secrets.$From
    # Trim any stray whitespace/BOM the value may carry; secrets are single-line.
    if ($value) { $value = $value.Trim([char]0xFEFF, [char]0x200B, ' ', "`t", "`r", "`n") }
    if ([string]::IsNullOrWhiteSpace($value)) {
        $script:missing += $From
        Write-Warning "$From is not in user-secrets - skipped."
        return
    }
    if (($existing -contains $To) -and -not $Overwrite) {
        Write-Host "  $To already set - skipped" -ForegroundColor DarkGray
        $script:skipped++
        return
    }
    if (-not $PSCmdlet.ShouldProcess("$TargetVault/$To", 'Set secret')) { return }

    # Passed with --value rather than --file: a file round-trip can add a BOM or a
    # trailing newline, and those characters end up INSIDE the secret — the app
    # then fails to decode the encryption key ("not a valid Base-64 string").
    az keyvault secret set --vault-name $TargetVault --name $To --value $value --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to set $To in $TargetVault." }

    # Read it back and compare, so a mangled value is caught here and not by a
    # crashing app three stages later.
    $stored = az keyvault secret show --vault-name $TargetVault --name $To --query value -o tsv
    if ($stored -ne $value) {
        throw "$To was stored differently than expected (length $($stored.Length) vs $($value.Length)). Check $TargetVault by hand."
    }

    Write-Host "  set $To" -ForegroundColor Green
    $script:set++
}

foreach ($name in $shared) { Copy-Secret -From $name -To $name }
foreach ($entry in $stripe.GetEnumerator()) { Copy-Secret -From $entry.Key -To $entry.Value }

Write-Host ""
Write-Host "Set: $set   Already present: $skipped" -ForegroundColor Cyan
if ($missing.Count) {
    Write-Warning "Not found in user-secrets: $($missing -join ', '). Add them to $TargetVault by hand."
}
Write-Host "Now re-run the pre-prod pipeline." -ForegroundColor Cyan
