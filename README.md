# RRIDA Backend API

Backend API for the **RRIDA** platform (formerly Clover ERA). It powers team health tracking from
daily check-ins, the RRIDA engine (team state, headwinds, suggested actions), the **ERAN** AI coach,
manager actions and timelines, portfolio views, and user, team, licence and subscription management.

---

## Contents

- [What it does](#what-it-does)
- [Tech stack](#tech-stack)
- [Solution structure](#solution-structure)
- [Getting started](#getting-started)
- [Database](#database)
- [Running the tests](#running-the-tests)
- [Background jobs](#background-jobs)
- [Environments and deployment](#environments-and-deployment)
- [Further documentation](#further-documentation)
- [Contributing](#contributing)

---

## What it does

| Area | What it covers |
|---|---|
| **Team Health** | Daily check-in questions, answers, team timeline, milestones, custom questions, weekly flags |
| **RRIDA engine** | Turns check-in answers into a daily team state (incl. Headwinds), recommends actions, nightly engine run |
| **ERAN coach** | AI coach that writes daily team messages and answers chat, using team data, engine output and manager actions |
| **Manager actions** | Managers create, edit, comment on and close their own team actions; engine suggestions taken up with "I'll do it" appear alongside |
| **Portfolio view** | Managers, Portfolio Managers and admins see their teams, alerts and action activity in one place |
| **Users and teams** | Profiles (companies), users, roles (RRIDA Admin, Profile Admin, Portfolio Manager, Team Manager), team structure and hierarchy |
| **Licensing and billing** | Trial and paid licences per Team Manager, plans, Stripe checkout and subscriptions, Azure Marketplace, promo codes |
| **Notifications** | Email (SendGrid), push notifications (OneSignal), calendar invites for daily check-ins |
| **Reports and AI** | Feedback analysis, insights, report generation and approval, document embeddings and RAG search |

---

## Tech stack

- **.NET 9** / ASP.NET Core Web API
- **PostgreSQL** with **Entity Framework Core 9** (Npgsql, pgvector)
- **JWT** authentication, Azure AD (Microsoft Identity)
- **Stripe** for payments, **SendGrid** for email, **OneSignal** for push
- **Azure OpenAI / LiteLLM** for ERAN and AI features, **Azure AI Search** for search
- **Azure App Service**, **Azure Key Vault**, **Application Insights**, Bicep templates for infrastructure
- **xUnit** + **Moq** + EF Core InMemory for tests
- Swagger (Swashbuckle) for API docs

---

## Solution structure

All code lives in `CloverBackoffice/` (`CloverBackoffice.sln`).

```
CloverBackoffice/
  CloverBackoffice.API             Web API: controllers, Program.cs, appsettings, background services
  CloverBackofficeServices         Business logic (services and interfaces)
  CloverBackofficeInfrastructure   Entities (Models) and DTOs
  CloverBackoffice.Repository      DbContext, EF configuration, migrations, SQL scripts
  CloverBackoffice.Components      Shared helpers, enums, response messages
  CloverBackoffice.Jobs            Scheduled / background job code
  CloverBackoffice.Logging         Logging helpers
  CloverBackoffice.Console         Console utilities
  CloverBackoffice.Tests           Automated tests
  external/RRIDA-Engine            Vendored RRIDA engine (see its DEVELOPER-GUIDE.md and VENDORED.md)
  docs/                            Engine integration plan and test guide, feature notes

infra/                             Bicep templates and database bootstrap for Azure
pipelines/                         Pipeline variables and shared templates
azure-pipelines-api.yml            Build, migrate and deploy pipeline for the API
azure-pipelines-infra.yml          Infrastructure pipeline
```

---

## Getting started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 15+ (local, or access to the Dev database)
- EF Core CLI: `dotnet tool install --global dotnet-ef`
- Visual Studio 2022 / VS Code / Rider

### 1. Clone and restore

```bash
git clone <repo-url>
cd <repo>/CloverBackoffice
dotnet restore
```

### 2. Configure secrets

Secrets are **never** committed. Locally they come from .NET user secrets (the API project has a
`UserSecretsId`); in Azure they come from Key Vault.

```bash
cd CloverBackoffice.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=rrida;Username=...;Password=..."
dotnet user-secrets set "Jwt:Key" "<your-dev-signing-key>"
# plus Stripe, SendGrid, OneSignal, LiteLLM / Azure OpenAI keys as needed
```

Ask a team member for the full list of keys used on Dev. Non-secret settings (job schedules,
feature flags, thresholds) are in `appsettings.json` / `appsettings.Development.json`.

### 3. Run the API

```bash
cd CloverBackoffice
dotnet run --project CloverBackoffice.API
```

Swagger UI is available at `https://localhost:<port>/swagger` in Development.

---

## Database

- Schema is managed by **EF Core migrations** in `CloverBackoffice.Repository/Migrations`.
- Apply migrations locally:

  ```bash
  dotnet ef database update --project CloverBackoffice.Repository --startup-project CloverBackoffice.API
  ```

- Add a migration after changing an entity:

  ```bash
  dotnet ef migrations add <Name> --project CloverBackoffice.Repository --startup-project CloverBackoffice.API
  ```

- **Idempotent SQL scripts** for each feature live in `CloverBackoffice.Repository/Scripts/<Area>/`
  (for example `TeamHealth/09_AddManagerActionStartDate.idempotent.sql`). Each one is the output of
  `dotnet ef migrations script ... --idempotent`, is safe to run more than once, and must be run
  **before** deploying code that needs it. Run them in number order within a folder.
- Migrations never run on app startup (background jobs share the database, so it would race).

---

## Running the tests

```bash
cd CloverBackoffice
dotnet test CloverBackoffice.Tests
```

Run one area only:

```bash
dotnet test CloverBackoffice.Tests --filter "FullyQualifiedName~ManagerAction"
```

> If the API is running locally it locks the build output. Stop it first, or build the tests
> somewhere else with `--artifacts-path <folder>`.

---

## Background jobs

Hosted services started by the API (schedules and on/off switches in `appsettings` under
`JobSchedules` and related sections):

| Job | Purpose |
|---|---|
| `EngineDailyRunBackgroundService` | Nightly RRIDA engine run for every team |
| `EngineDummyCheckInBackgroundService` | Test/demo only: answers daily check-ins for configured teams |
| `FeedbackAutoImportBackgroundService` | Imports feedback automatically |
| `DailyFeedbackAuditBackgroundService` / `TeamDailyFeedbackAuditBackgroundService` | Daily feedback audits |
| `CustomQuestionExpiryBackgroundService` | Expires custom team questions |
| `BatchProcessingBackgroundService` / `BatchPollingBackgroundService` | AI batch processing |
| `PromptfooBackgroundService` | Prompt evaluation runs |
| `CloverEraBackgroundService` | General scheduled platform tasks |

In Production, jobs are switched off in the staging slot.

---

## Environments and deployment

Azure DevOps pipelines (`azure-pipelines-api.yml`):

| Branch | Environment | Steps |
|---|---|---|
| `main` | **Dev** (`app-rrida-api-dev`) | build → migrate (EF bundle) → deploy |
| `pre-prod` | **Pre-Prod** (`app-rrida-api-preprod`) | build → provision infra → migrate → deploy |
| `prod` | **Production** | build → migrate → deploy to staging slot → swap |

Infrastructure is defined in `infra/` (Bicep, one parameter file per environment). See
`infra/README.md` for details.

---

## Further documentation

- `infra/README.md`: Azure infrastructure and database bootstrap
- `CloverBackoffice/docs/RRIDA-Engine-Integration-Plan.md`: how the engine is wired in
- `CloverBackoffice/docs/RRIDA-Engine-Test-Guide.md`: testing the engine
- `CloverBackoffice/external/RRIDA-Engine/DEVELOPER-GUIDE.md`: the engine itself
- Swagger UI on any running environment for the full endpoint list

---

## Contributing

1. Branch from `main` (`feature/<short-name>` or `fix/<short-name>`).
2. Keep changes small and add or update tests in `CloverBackoffice.Tests`.
3. If you change the schema: add an EF migration **and** an idempotent SQL script under
   `Scripts/<Area>/`, and note in the commit message that it must run before deploy.
4. Run `dotnet test` before opening a pull request.
5. Never commit secrets: use user secrets locally and Key Vault in Azure.
