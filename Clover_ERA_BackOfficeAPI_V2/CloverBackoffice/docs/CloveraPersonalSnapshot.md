# Clovera Personal App — Snapshot Report

## Overview

The Snapshot Report engine generates a single, structured "deep synthesis" of one
business unit's CLOVER engagement cycle for the **Clovera Personal App**. It loads
the unit's daily engagement check-ins from the database, sends them to Claude/GPT
(the configured default Search-model), and persists a parsed result containing a
headline, summary, spotlight pillar, and one tailored nudge.

It is modelled on the existing **Workbench / CloverEra Engine** flow (queue →
background worker → poll) and reuses the same LLM configuration source
(`ModelConfiguration`) and the same per-unit feedback loader
(`IFeedbackAnalysisService.FeedbackDataAsync`).

> **Status:** v1. Engagement-only (no productivity logs, no RAG/KB grounding). The
> `IsCloveraPersonalApp` access gate is temporarily disabled for testing — see
> [Known limitations](#known-limitations--todos).

---

## What it produces

A snapshot has five logical parts (mirrors the `clover-snapshot-handoff` reference):

| Part | Field(s) | Notes |
|------|----------|-------|
| Summary | `Headline`, `SummaryBody` | 2–4 word headline + ≤40-word body |
| Productivity observation | `ProductivityObservation` | **Empty in v1** (no productivity data in DB) |
| CLOVER spotlight | `HighlightedPillarCode/Label`, `HighlightedSmileyScore`, `HighlightReason`, `PillarFraming` | Pillar chosen **server-side**; LLM only frames it |
| Nudge | `NudgeTitle`, `NudgeRationale`, `NudgeAction`, `NudgeIntent`, `DrawingFromJson` | One concrete, non-obvious action |
| Telemetry | `ModelUsed`, `PromptTokens`, `CompletionTokens`, `TotalTokens`, `GenerationSeconds` | For cost/ops visibility |

---

## Architecture

```
┌──────────────┐   POST /run    ┌────────────────────────────┐
│  Caller      │ ─────────────▶ │ CloveraPersonalSnapshot     │
│  (mobile/web)│ ◀───────────── │ Controller                  │
└──────────────┘  202 {id}      └─────────────┬──────────────┘
       │ GET /{id}/status                     │ enqueue {SnapshotId}
       │                                       ▼
       │                         ┌────────────────────────────┐
       │                         │ CloveraPersonalSnapshot     │
       │                         │ WorkQueue (bounded, 20)     │
       │                         └─────────────┬──────────────┘
       │                                       ▼
       │                         ┌────────────────────────────┐
       │                         │ ...BackgroundService        │
       │                         │  Running → Completed/Failed │
       │                         └─────────────┬──────────────┘
       │                                       ▼
       │                         ┌────────────────────────────┐
       │                         │ ...Service.GenerateAsync    │
       │                         │  1. default Search-model    │
       │                         │  2. load DailyFeedback      │
       │                         │  3. pickHighlight (pillar)  │
       │                         │  4. build prompt            │
       │                         │  5. call LLM                │
       │                         │  6. parse JSON → entity     │
       │                         └─────────────┬──────────────┘
       │                                       ▼
       └───────── reads ───────▶  public."CloveraPersonalSnapshots"
```

The request returns immediately with a `SnapshotId`; generation (Claude ~8–15s)
runs in the background. The caller polls the status endpoint until `Completed`.

---

## Components

| File | Role |
|------|------|
| `API/Controllers/CloveraPersonalSnapshotController.cs` | `POST /run`, `GET /{id}/status`. Validation, feature gate, enqueue. |
| `API/Middlewares/TokenAuthentication.cs` | Route added to `skipPaths` (auth bypass). |
| `Services/Interfaces/ICloveraPersonalSnapshotService.cs` | Service contract. |
| `Services/Implementation/CloveraPersonalSnapshotService.cs` | Core: data load, pillar pick, prompt, LLM call, parse. Holds the `SystemPrompt`. |
| `Services/Implementation/CloveraPersonalSnapshotBackgroundService.cs` | Queue + work item + hosted worker (run lifecycle + persistence). |
| `Infrastructure/DTO/CloveraPersonalSnapshotRequestDto.cs` | Request body. |
| `Infrastructure/Models/CloveraPersonalSnapshot.cs` | Result/run entity + `DbSet` in `ApplicationDbContext`. |
| `Repository/Scripts/CreateCloveraPersonalSnapshots.sql` | Table DDL (run manually; no EF migration). |
| `API/Program.cs` | DI registrations. |

---

## API contract

Base route: `api/clovera-personal-snapshot`. Auth is **disabled** for this controller
(see [Security](#security)).

### POST `/api/clovera-personal-snapshot/run`

Request body:

```jsonc
{
  "companyId": 71,            // required, > 0
  "unitId": 166,             // required, > 0
  "fromDate": "2026-05-01",  // required (ISO date/offset)
  "toDate":   "2026-05-27"   // required, must be >= fromDate
}
```

Model, temperature, and token limits are **not** accepted — the service selects the
default active Search-model and uses fixed generation settings.

Response — `202 Accepted`:

```jsonc
{
  "isSuccess": true,
  "message": "Snapshot generation queued.",
  "content": { "snapshotId": 123, "status": "Queued" }
}
```

Validation failures return `200` with `isSuccess: false` (house `ApiResponseHandler`
convention), e.g. missing fields, `FromDate > ToDate`, unit not found.

### GET `/api/clovera-personal-snapshot/{id}/status`

While `Queued`/`Running`/`Failed`:

```jsonc
{
  "isSuccess": true,
  "message": "Snapshot status: Running",
  "content": {
    "id": 123, "status": "Running",
    "companyId": 71, "unitId": 166,
    "fromDate": "...", "toDate": "...",
    "errorMessage": null, "createdAt": "...", "completedAt": null
  }
}
```

When `Completed`:

```jsonc
{
  "isSuccess": true,
  "message": "Snapshot status: Completed",
  "content": {
    "id": 123, "status": "Completed",
    "companyId": 71, "unitId": 166, "fromDate": "...", "toDate": "...",
    "modelUsed": "gpt-4o",
    "summary": { "headline": "...", "body": "..." },
    "productivityObservation": "",
    "highlightedPillar": {
      "code": "O", "label": "Opportunities",
      "smileyScore": 1, "reasonForHighlight": "lowest"
    },
    "nudge": {
      "title": "...", "rationale": "...", "action": "...",
      "intent": "challenge", "drawingFromJson": "[...]"
    },
    "promptTokens": 0, "completionTokens": 0, "totalTokens": 0,
    "generationSeconds": 11.4,
    "createdAt": "...", "completedAt": "..."
  }
}
```

### Read endpoints (per-unit)

These mirror the admin workbench's history/list/latest trio, but are **scoped to a
single unit** — the personal app only ever shows a unit its own snapshots, so
`unitId` (the business unit id) is **required** on each. Missing/invalid → `"Unit ID
is required."` All three are anonymous (same prefix exemption as the rest of the
controller).

| Endpoint | Mirrors (admin) | Returns |
|----------|-----------------|---------|
| `GET /personal-history?unitId=` | `workbench-history` | Last 15 days for the unit, newest first; each row = run metadata + full `Result` (when Completed) |
| `GET /run-list?unitId=` | `workbench-run-list` | Last 15 days for the unit, lightweight: `Id, Status, CompanyId, UnitId, CreatedAt, CompletedAt` |
| `GET /latest-run-insights?unitId=` | `latest-run-insights` | The unit's most recent **Completed** snapshot, full insight payload |

The `Completed` payload shape (in `status`, `personal-history.Result`, and
`latest-run-insights`) is produced by shared helpers so it stays identical across all
three.

---

## Data sources & logic

### Engagement load

`IFeedbackAnalysisService.FeedbackDataAsync(companyId, unitId, fromDate, toDate)`
returns `FeedbackDataResultDto` rows from `DailyFeedback` joined to question, reason,
and `DailyQuestionSpecification` (for the pillar).

The service reduces these to **one entry per CLOVER pillar** (latest submission per
pillar within the window).

| Source field | Snapshot use |
|--------------|--------------|
| `CategoryId` (1–6) | Pillar → `C/L/O/V/E/R` |
| `SentimentTypeId` (1–4) | Smiley score (1 Angry … 4 Happy) |
| `ReasonText` | Dropdown reason |
| `FreeText` | The user's own words |
| `QuestionText` | The ERA question shown |

Pillar mapping: `Communication=C`, `Learning=L`, `Opportunities=O`,
`Vulnerability=V`, `Enablement=E`, `Reflection=R`.

### Spotlight pillar (`PickHighlight`)

Deterministic, server-side (port of `pickHighlight` from the reference engine, minus
the prior-cycle "biggest mover" branch which is not wired in v1):

1. Lowest-scoring pillar with smiley `<= 2` → `lowest`
2. Else highest-scoring pillar with smiley `>= 4` → `highest`
3. Else fall back to lowest

Nudge `intent` is derived: `highest`/`biggest_mover_up` → `momentum`;
score `<= 2` → `challenge`; otherwise `support`.

---

## Model selection & LLM call

The model is the **default active Search-model**:
`IDocumentEmbeddingRepository.GetDefaultModelAsync(modelConfigTypeId: 1)` — the same
source `RagService` and `FeedbackAnalysisService` use. If none exists the run fails
with *"No default active Search-model found in ModelConfiguration."*

Fixed generation settings: **temperature 0.65, max_tokens 1200, top_p 1.0**.

`CallLlmAsync` routes by endpoint shape (mirrors the existing engine + `RagService`):

| Endpoint contains | Path | Body |
|-------------------|------|------|
| `/anthropic/` | POST endpoint, headers `x-api-key` + `anthropic-version: 2023-06-01` | `{ model, system, messages, max_tokens, (temperature\|top_p) }` |
| `/models/chat/completions` or `.services.ai.azure.com/.../models/` (AI Foundry serverless) | POST endpoint, header `api-key` | `{ model, messages, max_tokens, temperature, top_p }` |
| otherwise (Azure OpenAI, e.g. default `gpt-4o` on `cognitiveservices.azure.com`) | base address + `/openai/deployments/{model}/chat/completions?api-version={ApiVersion}`, header `api-key` | `{ messages, max_tokens, temperature, top_p }` |

The response is parsed as strict JSON (markdown ```json fences stripped if present);
an unparseable body fails the run with a truncated preview.

---

## System prompt

Adapted from the `clover-snapshot-handoff` `route.ts` `SYSTEM_PROMPT`. All discipline
rules are retained: mobile word budgets, headline specificity, **agency-location**
(do not victim-blame on systemic failures), **safety caveat** for upward-challenge
moves, quotation discipline, context-invention discipline, second-person POV, no
motivational closers.

Differences for v1:
- KB/`[clover-content]` grounding removed (no RAG). The nudge is synthesised from
  CLOVER-framework knowledge; `nudge_drawing_from` may be an empty array.
- No productivity logs → the model is instructed to return an empty
  `productivity_observation`.

The prompt is a constant in `CloveraPersonalSnapshotService`. Treat it as IP; if you
change a rule, re-test against the reference scenarios.

---

## Database

Table `public."CloveraPersonalSnapshots"` is created by
`CloverBackoffice.Repository/Scripts/CreateCloveraPersonalSnapshots.sql` and run
**manually** — there is intentionally **no EF migration** (to avoid bundling the
unrelated, un-migrated "licenses" backlog into a snapshot migration). The
`DbSet<CloveraPersonalSnapshot>` is registered in `ApplicationDbContext`; at runtime
EF maps to the table the script creates.

Key columns: `Id` (identity PK), `Status`, `CompanyId`, `UnitId`, `FromDate`,
`ToDate`, `ModelUsed`, the parsed output fields, token/seconds telemetry,
`ErrorMessage`, `IsDel`, `CreatedAt`, `CompletedAt`, `CreatedBy`.

> If a future `dotnet ef migrations add` is run, EF will sweep this table (and the
> licenses backlog) into that migration. Keep that in mind.

---

## Dependency injection (`Program.cs`)

```csharp
builder.Services.AddSingleton<CloveraPersonalSnapshotWorkQueue>();
builder.Services.AddHostedService<CloveraPersonalSnapshotBackgroundService>();
builder.Services.AddScoped<ICloveraPersonalSnapshotService, CloveraPersonalSnapshotService>();
```

Reused services: `IFeedbackAnalysisService`, `IDocumentEmbeddingRepository`,
`IHttpClientFactory`, `ApplicationDbContext`.

---

## Testing

1. Create the table: run `Scripts/CreateCloveraPersonalSnapshots.sql` against the DB.
2. Ensure a default Search-model exists (`ModelConfiguration` where
   `ModelConfigTypeId = 1` and `IsDefault = true`) with a valid key/endpoint.
3. Pick a unit with engagement data spanning the CLOVER pillars. On the dev DB,
   **company 71 ("Clover ERA"), units 166 / 299 / 300** each have all six pillars
   with recent data.
   - When the `IsCloveraPersonalApp` gate is re-enabled, the unit must have that flag:
     `UPDATE "BusinessUnits" SET "IsCloveraPersonalApp" = true WHERE "Id" = 166;`
4. `POST /api/clovera-personal-snapshot/run` with `{ companyId, unitId, fromDate, toDate }`.
5. Poll `GET /api/clovera-personal-snapshot/{id}/status` until `Completed` (or `Failed`
   with `errorMessage`).

A real run calls the live model and bills the configured key.

---

## Security

- The controller is `[AllowAnonymous]` **and** its route is in
  `TokenAuthentication.skipPaths` — mirroring how `/api/prompt-management/workbench-run`
  is exposed. There is **no auth** on these endpoints currently.
- Before exposing publicly, decide the real auth model (token + unit-scoping) and
  whether the snapshot should be restricted to the owning company/unit.

---

## Known limitations & TODOs

- **`IsCloveraPersonalApp` gate is commented out** in the controller (testing). Re-enable
  before go-live (search for the `TODO` in `CloveraPersonalSnapshotController`).
- **Engagement-only.** No productivity logs exist in the DB; `ProductivityObservation`
  is empty. Add a productivity source to populate it.
- **No RAG/KB grounding.** Nudges are synthesised from framework knowledge only. Wire
  the pgvector/DocumentEmbedding store for KB-grounded nudges (and `drawing_from`
  citations) when desired.
- **No idempotency.** Each `run` creates a new row and a new (billed) LLM call. Add a
  one-snapshot-per-(unit, cycle) guard for production.
- **No prior-cycle comparison.** The "biggest mover" spotlight branch is not wired.
- **Auth** — see above.
```
