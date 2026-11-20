<#
.SYNOPSIS
    Previews or deploys the RRIDA infrastructure for one environment.

.EXAMPLE
    $env:RRIDA_PG_ADMIN_PASSWORD = '<strong-password>'
    ./deploy.ps1 -Environment preprod        # what-if preview, changes nothing
    ./deploy.ps1 -Environment preprod -Apply # actually deploys
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'preprod', 'prod')]
    [string]$Environment,

    [string]$Location = 'eastus2',

    [string]$SubscriptionId,

    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$infraRoot = $PSScriptRoot
$template = Join-Path $infraRoot 'main.bicep'
$parameters = Join-Path $infraRoot "params/$Environment.bicepparam"

if (-not $env:RRIDA_PG_ADMIN_PASSWORD) {
    throw "RRIDA_PG_ADMIN_PASSWORD is not set. The Postgres admin password is never stored in this repo."
}

if ($SubscriptionId) {
    az account set --subscription $SubscriptionId
}

$account = az account show --query "{name:name, id:id}" -o tsv
Write-Host "Target subscription: $account" -ForegroundColor Cyan
Write-Host "Environment:         $Environment" -ForegroundColor Cyan

$deploymentName = "rrida-$Environment"

if ($Apply) {
    Write-Host "Deploying..." -ForegroundColor Yellow
    az deployment sub create `
        --name $deploymentName `
        --location $Location `
        --template-file $template `
        --parameters $parameters `
        --query "properties.outputs" `
        -o jsonc
}
else {
    Write-Host "What-if preview (no changes applied). Re-run with -Apply to deploy." -ForegroundColor Yellow
    az deployment sub what-if `
        --name $deploymentName `
        --location $Location `
        --template-file $template `
        --parameters $parameters
}
