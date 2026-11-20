# RRIDA infrastructure

Bicep templates for the RRIDA footprint in Azure. One template, one parameter
file per environment. State lives in Azure Resource Manager — there is nothing
to store, lock or import.

```
infra/
  main.bicep              subscription-scope entry point, creates the resource group
  modules/
    monitoring.bicep      Log Analytics workspace + Application Insights
    keyvault.bicep        RBAC-only vault (no secret values, ever)
    postgres.bicep        Flexible Server, database, azure.extensions, firewall
    appservice.bicep      Linux plan + API web app (+ staging slot in prod)
    rbac.bicep            role grants for the app identities
  params/
    dev.bicepparam        B1ms / B1 plan / Swagger on / cheapest thing that runs
    preprod.bicepparam    B2s / B2 plan / Swagger on / no HA / no slot
    prod.bicepparam       D2ds_v5 / P0v3 / ZoneRedundant / staging slot
  db/bootstrap/
    01_schema.sql         schema baseline, applied only to an EMPTY database
    02_reference_data.sql lookup rows + __EFMigrationsHistory (no tenant data)
    bootstrap-database.sh run by the pipeline before every EF migration
    export-bootstrap.sh   regenerates the two SQL files from Dev
  deploy.ps1              what-if / deploy wrapper
```

Pipelines live at the repo root: `azure-pipelines-infra.yml`,
`azure-pipelines-api.yml`, with per-environment values in
`pipelines/variables/` and the shared provisioning steps in `pipelines/templates/`.

## Branches and environments

| Branch | Environment | What a push does |
|---|---|---|
| `main` | Dev | API pipeline: build -> migrate -> deploy `app-rrida-api-dev`. Infra pipeline deploys `dev.bicepparam` when `infra/**` changes. |
| `pre-prod` | Pre-Prod | API pipeline: build -> **provision** (`preprod.bicepparam`, seeds Key Vault) -> **bootstrap + migrate** database -> deploy `app-rrida-api-preprod` -> `/health` smoke test. |

Pre-Prod needs no manual infrastructure work after the one-time setup below.

## Pre-Prod one-time setup (Azure DevOps)

1. **Variable group** `rrida-preprod` (Pipelines > Library):
   - `RRIDA_PG_ADMIN_PASSWORD` — secret. Pick a strong password and keep it:
     every deploy sets the Postgres admin password to this value.
   - `RRIDA_FRONTEND_BASE_URL` — optional, e.g. `https://rrida-preprod.azurewebsites.net`.
     Sets email links, Stripe return URLs and the first CORS origin.
2. **Resource group**: create `rg-rrida-preprod-001` (East US 2) by hand — the
   Pre-Prod service connection is scoped to it and cannot create it.
3. **Service connection** `rrida-preprod-arm` (Project settings > Service
   connections > Azure Resource Manager), scoped to subscription
   `eng-development-001` **and** resource group `rg-rrida-preprod-001`. Dev's
   `rrida-dev-arm` is not reused: it has no rights outside the Dev group, which
   is what made a subscription-scope deploy fail with `AuthorizationFailed`.
4. **Environment** `rrida-preprod` (Pipelines > Environments > New, resource
   "None"), then Security > Pipeline permissions > add the API pipeline. Add an
   approval check here if Pre-Prod deploys should be gated.
5. **Shared secrets**: the scoped connection cannot read the Dev vault, so after
   the first Provision stage has created `kv-rrida-preprod-001`, copy the shared
   secrets into it by hand (list below). The pipeline stops with a clear error
   until `JwtSettingsKry` is there.

Then push to `pre-prod`. The first run fills `rg-rrida-preprod-001` (about
10–15 minutes, most of it Postgres).

**Why Pre-Prod uses `main-rg.bicep`.** A service connection scoped to one
resource group can neither create resource groups nor create role assignments.
Pre-Prod therefore deploys at resource-group scope into the group above, and its
vault uses **access policies** instead of RBAC — both within the Contributor
rights that connection already has. Dev and Prod keep `main.bicep`, subscription
scope and Key Vault RBAC.

### What the Pre-Prod pipeline does on its own

- **Key Vault** — grants the app and the pipeline access, then writes
  `RRIDA-PREPROD` (connection string built from the new server and
  `RRIDA_PG_ADMIN_PASSWORD`). It also tries to copy the shared secrets from
  `kv-rrida-dev-001`, which only works for a connection that can read that vault;
  with the scoped Pre-Prod connection it cannot, and the log lists what to add by
  hand:

  | Copy from `kv-rrida-dev-001` | Into `kv-rrida-preprod-001` |
  |---|---|
  | `JwtSettingsKry` (required) | same name |
  | `EncryptionIV`, `EncryptionKey` | same name |
  | `SendGrid`, `ResendApiKey` | same name |
  | `AzureRagSearch`, `ApiSecretKey`, `AzureMarketplaceClientSecret`, `OpenAI` | same name |
  | `Dev-StripeSecretKey` / `Dev-StripePublishableKey` / `Dev-StripeWebhookSecret` | `PreProd-StripeSecretKey` / `PreProd-StripePublishableKey` / `PreProd-StripeWebhookSecret` |

  A secret is only ever written **when it is missing**, so a value set by hand in
  `kv-rrida-preprod-001` is never overwritten.
- **Database** — `bootstrap-database.sh` creates the `vector` extension, and on
  an empty database loads `01_schema.sql` + `02_reference_data.sql`. The EF
  bundle then applies every migration newer than the baseline. On later runs
  only new migrations apply, exactly like Dev. The EF migrations alone cannot
  build the database from nothing (the original schema came from hand-run
  scripts), which is why the baseline exists.
- **App** — `ASPNETCORE_ENVIRONMENT=PreProd` and `Environment:Name=PreProd`, so
  it loads `appsettings.PreProd.json`, reads `RRIDA-PREPROD` and the
  `PreProd-Stripe*` secrets, and shows Swagger at `/`. App Service health check
  and the smoke test use the anonymous `/health` endpoint.
- **First login** — the startup seeder creates the RRIDA super admin in the
  empty database, as it did on Dev.

## What it creates

| Resource | Dev | Pre-Prod | Prod |
|---|---|---|---|
| Resource group | `rg-rrida-dev-001` | `rg-rrida-preprod-001` | `rg-rrida-hosting-001` |
| Postgres Flexible 16 | `pg-rrida-dev-001` (B1ms, 32 GB) | `pg-rrida-preprod-001` (B2s, 64 GB) | `pg-rrida-eastus2-001` (D2ds_v5, 128 GB, ZoneRedundant) |
| App Service plan | `asp-rrida-dev-001` (B1) | `asp-rrida-preprod-001` (B2) | `asp-rrida-eastus2-prod-001` (P0v3) |
| API | `app-rrida-api-dev` | `app-rrida-api-preprod` | `app-rrida-api-001` + `staging` slot |
| Key Vault | `kv-rrida-dev-001` | `kv-rrida-preprod-001` | `kv-rrida-001` |
| Monitoring | `log-rrida-dev-001`, `appi-rrida-dev-001` | `log-rrida-preprod-001`, `appi-rrida-preprod-001` | `log-rrida-001`, `appi-rrida-001` |

Dev runs roughly $35/month at these SKUs.

The API gets a **system-assigned managed identity**
with `Key Vault Secrets User`. `Program.cs` already falls back to
`DefaultAzureCredential()` when no client secret is present, so no code change
is needed — just never set `AzureAd__ClientSecret` on the app.

## Deploy by hand

```powershell
az login
az account set --subscription eng-development-001

$env:RRIDA_PG_ADMIN_PASSWORD = Get-Content D:\Secrets\RRIDA\pg-rrida-dev-001.admin.txt -Raw

./deploy.ps1 -Environment dev             # what-if preview, changes nothing
./deploy.ps1 -Environment dev -Apply      # deploy
```

**Which subscription.** Dev lives in `eng-development-001`
(`8b1e7bc0-22e9-4600-bb24-5742a2ad4189`), where the team has Contributor +
User Access Administrator at subscription scope — both are required, because
this template creates resource groups and role assignments.

Pre-Prod lives in the same subscription as Dev, in its own resource group.

Prod targets `eng-hosting-001`, where current access is Owner on
`rg-cloverera-hosting-001` only. Deploying `prod.bicepparam` there needs someone to either grant subscription-scope
Contributor + User Access Administrator, or pre-create
`rg-rrida-hosting-001` and grant Owner on it (the template would then need
converting to resource-group scope).

The same password must be passed on every later deploy — the template sets the
Postgres admin password each time, so a different value silently rotates it.

## After the first deploy

1. **Seed the vault.** Bicep creates the vault and the role grants, never the
   secret values. Names come from `AzureKeyVault:SecretNames` in
   `appsettings.json`:

   ```powershell
   $kv = 'kv-rrida-dev-001'
   $fqdn = 'pg-rrida-dev-001.postgres.database.azure.com'

   az keyvault secret set --vault-name $kv --name RRIDA-DEV --value `
     "Host=$fqdn;Database=rrida;Username=rridaadmin;Password=<pw>;SSL Mode=Require;Trust Server Certificate=true"

   az keyvault secret set --vault-name $kv --name JwtSettingsKry              --value '<...>'
   az keyvault secret set --vault-name $kv --name EncryptionIV                --value '<...>'
   az keyvault secret set --vault-name $kv --name EncryptionKey               --value '<...>'
   az keyvault secret set --vault-name $kv --name SendGrid                    --value '<...>'
   az keyvault secret set --vault-name $kv --name ResendApiKey                --value '<...>'
   az keyvault secret set --vault-name $kv --name AzureRagSearch              --value '<...>'
   az keyvault secret set --vault-name $kv --name ApiSecretKey                --value '<...>'
   az keyvault secret set --vault-name $kv --name AzureMarketplaceClientSecret --value '<...>'
   az keyvault secret set --vault-name $kv --name Dev-StripeSecretKey         --value '<...>'
   az keyvault secret set --vault-name $kv --name Dev-StripePublishableKey    --value '<...>'
   az keyvault secret set --vault-name $kv --name Dev-StripeWebhookSecret     --value '<...>'
   ```

   **`JwtSettingsKry` is mandatory.** `Program.cs:111` does
   `Encoding.UTF8.GetBytes(jwtSettingsKey)` with no null guard, so a missing
   secret crashes the app at startup. The misspelling is what the code expects;
   match it exactly. Missing Stripe secrets only log a warning.

2. **Create the vector extension** in the database (the template allowlists it
   at server level; it still has to be created per database):

   ```sql
   CREATE EXTENSION IF NOT EXISTS vector;
   ```

3. **Restore the CloverEra dump** (dev seeds from production data):

   ```powershell
   $env:PGPASSWORD = '<postgres admin password>'
   ./db/restore-dev.ps1
   ```

   It opens a firewall rule for your IP, restores
   `D:\DB_Backups\CloverEra_prod_20260819_111905.dump` into `rrida`, baselines
   the EF migration history, scrubs personal data, verifies, then removes the
   rule. `-SkipScrub` keeps the real data; `-KeepFirewallRule` leaves your IP
   allowed for follow-up work.

   Three things about that dump worth knowing:

   - **It carries no `__EFMigrationsHistory`.** Production Clovera was built by
     hand-run SQL scripts. Without `db/baseline-migrations.sql`, the pipeline's
     migrate stage tries to apply all 15 migrations onto a schema that already
     has those objects and fails on the first `CREATE`.
   - **Two schemas come across**: `public` (79 tables, what EF uses) and
     `export` (13 copies of core tables). `cloverera_admin` is the owning role,
     not a schema, so no `search_path` override is needed. The `export` copies
     include `Companies`, `Contacts` and `DailyFeedback`, so the scrub covers
     both schemas.
   - **It is production data.** `db/scrub-pii.sql` replaces emails, names,
     company names, employee free text and Stripe ids while keeping row counts,
     foreign keys, sentiment and dates intact, so scores and the report loop
     still behave. It refuses to run against a database whose name looks like
     production.

4. **Migrations** run from the pipeline. To do it by hand, add your IP to
   `clientIpAllowList` in the param file, redeploy, then:

   ```powershell
   $env:ConnectionStrings__DefaultConnection = '<connection string>'
   dotnet ef database update --project ../CloverBackoffice/CloverBackoffice.Repository `
                             --startup-project ../CloverBackoffice/CloverBackoffice.API
   ```

   Never `Database.Migrate()` on startup — the API can run on more than one
   instance, and every instance boots its hosted background jobs against the
   same database, so a startup migration would race.

5. **Smoke test**: `https://app-rrida-api-dev.azurewebsites.net/` serves Swagger
   UI, because dev sets `ASPNETCORE_ENVIRONMENT=Development` and `Program.cs`
   gates Swagger on `IsDevelopment()`.

## CI/CD setup (once)

1. **Service connection.** Project settings → Service connections → New →
   Azure Resource Manager → Workload identity federation (automatic). Scope it
   to subscription `eng-hosting-001`, name it **`rrida-dev-arm`** (matches the
   `azureServiceConnection` variable in all three pipelines).

   It needs **Contributor** *and* **User Access Administrator** on the
   subscription or on `rg-rrida-dev-001` — the template creates role
   assignments, which Contributor alone cannot do.

2. **Variable group.** Pipelines → Library → new group **`rrida-dev`** with one
   secret variable `RRIDA_PG_ADMIN_PASSWORD`. Allow the infra pipeline to use it.

3. **Grant the pipeline vault access.** The migrate stage reads the connection
   string from Key Vault. Get the service connection's principal object id and
   put it in `params/dev.bicepparam` as `pipelinePrincipalId`, then redeploy:

   ```powershell
   az ad sp show --id <service-connection-appId> --query id -o tsv
   ```

4. **Create the pipelines.** Pipelines → New → Azure Repos Git → this repo →
   Existing YAML file. Add both:
   - `azure-pipelines-infra.yml` — what-if on PR, deploy on merge to main
   - `azure-pipelines-api.yml` — build → migrate → deploy API → smoke test

5. **Environment.** The deployment jobs target an environment named
   `rrida-dev`; Azure DevOps creates it on first run. Add approvals there later
   when the same pipelines point at prod.

### How the migrate stage reaches the database

Microsoft-hosted agents get a fresh public IP per run, and the
`AllowAllAzureServices` firewall rule does not reliably cover them. The stage
opens a firewall rule for its own IP, runs a self-contained EF bundle, and
deletes the rule in a step with `condition: always()`.

## Deliberately not in these templates

- **Secret values** — vault seeded by hand or by a pipeline task.
- **Entra app registrations** (`AzureAd:ClientId`, API audience, SPA client).
  Graph objects, not ARM resources. Create separately, then feed the ids in
  through `additionalApiAppSettings`.
- **Azure OpenAI and AI Search** — reused from the existing Clovera resources.
  Give RRIDA its own index name rather than a second service.
- **Custom domains and certificates** — add once DNS is decided.
- **Private endpoints / VNet integration** — servers start with public access
  plus the Azure-services rule. Tighten before prod carries customer data.

## Notes

- Postgres child resources (extensions, database, firewall rules, Entra admin)
  are chained with `dependsOn` on purpose: the control plane rejects concurrent
  writes against the same server.
- `alwaysOn` is on for the API because every background job runs in-process as
  a hosted `BackgroundService`; without it the host unloads when idle and the
  scheduled jobs stop firing.
- There is no Function App. `CloverBackoffice.Jobs` and its `func-rrida-jobs-*`
  App Service (plus the `strridajobs*` runtime storage account) were removed;
  its 5-minute timer trigger is now `BatchPollingBackgroundService` inside the
  API, on the same cadence.
