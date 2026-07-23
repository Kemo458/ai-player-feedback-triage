# AI-Powered Player Feedback Triage System

Working implementation of the spec in [`docs/SPEC.md`](docs/SPEC.md). Game teams import Google Play
reviews and internal feedback, a local LLM classifies each item asynchronously (category, severity,
toxicity, sentiment, one-sentence summary, game entities), and Community Managers triage everything
on a realtime, keyboard-friendly dashboard. The dashboard prioritizes actionable queues, keeps
ingestion controls in a dedicated drawer, supports entity/theme filtering and triage sorting, and
uses reduced-motion-aware feedback for live processing events. Google Play games display their
store artwork, internal-feedback collection is a first-class dashboard action, and a compact live
operations rail makes the import → Qwen → ready workflow visible.
Each game has one permanent internal-feedback link: managers can copy it again after a reload, and
opening the link never rotates or invalidates it.

## Stack
- **Backend** — ASP.NET Core 8 Web API + background workers, EF Core / PostgreSQL, SignalR.
- **PostgreSQL** — system of record **and** durable job queue (`FOR UPDATE SKIP LOCKED`).
- **LLM** — Ollama with JSON-schema structured output; a deterministic offline mock analyzer.
- **Scraper** — isolated Python FastAPI sidecar using pinned `google-play-scraper==1.2.7`.
- **Frontend** — React + TypeScript + Vite + TanStack Query + SignalR.
- **Proxy** — Caddy fronts everything on one origin. **Docker Compose** ties it together.

## Architecture and resilience

Feedback is committed to PostgreSQL before analysis and returns immediately to the caller. Background
workers claim durable queue rows with `FOR UPDATE SKIP LOCKED`, attach processing leases, and recover
expired work after a restart. LLM calls therefore never hold open the submission request.

Transient timeouts and upstream `429`/`502`/`503`/`504` failures are rescheduled with bounded
backoff and jitter. Malformed or schema-invalid model output is retried once and then routed to
manual review. Every successful response is validated again in application code before storage.
SignalR accelerates dashboard refreshes, while REST and PostgreSQL remain authoritative if the
realtime connection drops.

## Prompt design

The Ollama system prompt treats player text as untrusted data and explicitly refuses instructions
inside the delimited `<feedback>` block. It defines the category, multi-label, severity, toxicity,
sentiment, summary, and entity rules; includes compact ambiguous examples; and sends a strict JSON
schema through Ollama's structured-output `format` field. Generation uses temperature `0` and a
fixed seed for repeatability.

Post-generation validation checks schema version, enums, ranges, summary length and sentence count,
category/tag consistency, and the rule that `Critical` requires a bug. Entity evidence must occur in
the original normalized feedback, which drops unsupported model inventions. Low-confidence or
conflicting results are sent to manual review. See
[`AnalysisSchema.cs`](backend/src/PlayerFeedback.Core/Analysis/AnalysisSchema.cs) and
[`AnalysisValidator.cs`](backend/src/PlayerFeedback.Core/Analysis/AnalysisValidator.cs).

## Quick start (local)
```bash
cp .env.example .env        # copy the public-safe, ready-to-run local template
docker compose up -d --build
open http://localhost:8090
```
Sign in with `manager` / `manager-dev-pass` (from `.env`). A demo game **Royal Match (Demo)** is
seeded with sample feedback; the copied `.env` uses the **Mock** analyzer, so classifications appear
within seconds with no model download.

Public feedback form for the demo game:
`http://localhost:8090/submit/<gameId>?token=demo-submission-token` (the gameId is shown in the games list).

### Using the real LLM locally
Set `LLM_PROVIDER=Ollama` in `.env`, then:
```bash
docker compose up -d
docker compose exec ollama ollama pull qwen2.5:3b
```
Restart the API after changing providers:
```bash
docker compose restart api
```

## Endpoints
- App / dashboard: `http://localhost:8090`
- OpenAPI: `http://localhost:8090/swagger`
- Health: `/health/live`, `/health/ready`, `/health/dependencies`

## Layout
```
backend/     ASP.NET Core — PlayerFeedback.Core (domain/data/analysis/scraping) + PlayerFeedback.Api (host/controllers/hubs/workers)
scraper/     Python FastAPI google-play-scraper sidecar (+ pytest)
frontend/    React SPA (Vite)
deploy/      OVH + Cloudflare tunnel deployment (see deploy/README.md)
docs/SPEC.md the full specification
```

## Deployment
Deployed to an OVH host behind a Cloudflare Tunnel at **https://epam.valentingrozdev.space**.
See [`deploy/README.md`](deploy/README.md).

## Deviations from the spec (and why)
- **Model**: the env-configurable deployment uses `qwen2.5:3b`, selected for predictable
  structured JSON and acceptable throughput on the shared CPU-only interview host.
- **Project layout**: the 6 planned .NET projects are consolidated into 2 (`Core` + `Api`) with the
  same layering, to keep the Docker build fast and reliable. Clean-architecture boundaries are kept
  at the folder level.
- **Schema management**: uses EF Core `EnsureCreated()` at startup instead of migration files (no
  local dotnet-ef needed; the container creates the schema on first boot). Production would switch to
  migrations.
- **Auth**: a real JWT bearer flow (login → token → `[Authorize]`) backed by a configured manager
  credential + role claims, rather than a full external OIDC provider.
- **Pagination**: opaque **offset** cursors (the client treats them as opaque tokens per the contract).

## Trade-offs and current limits

- PostgreSQL doubles as the durable queue. This avoids operating RabbitMQ for the interview-sized
  workload, at the cost of database polling and less sophisticated routing.
- The deployment uses custom bounded retry scheduling rather than Polly. The behavior is explicit
  and persisted with each job, but a production service could add circuit breaking and richer
  telemetry with `Microsoft.Extensions.Http.Resilience`.
- `qwen2.5:3b` is viable on the CPU-only host and follows the JSON schema reliably, but a larger
  model or labelled evaluation dataset would be needed before making quality claims.
- `EnsureCreated()` and idempotent schema patches keep the demo simple. Production would use reviewed
  EF migrations and versioned rollbacks.
- JWT manager authentication is sufficient for the assessment; production would use external OIDC,
  tenant isolation, secret rotation, and explicit submission-token revocation.
- The scraper has automated pytest coverage. Focused .NET worker/controller tests, frontend tests,
  end-to-end tests, public rate limiting, and OpenTelemetry remain future hardening rather than
  completed features.

## AI-assistant usage

Claude and OpenAI Codex were used throughout this assessment. Claude helped iterate on dashboard
information hierarchy, original-feedback visibility, Google Play reviewer attribution, and per-game
deletion. Codex reviewed and completed the architecture and documentation, selected and tuned the
local Qwen deployment, diagnosed summary failures, improved the dashboard and permanent submission
links, audited the deployed system, and prepared the credential-safe public repository.

AI-generated suggestions were reviewed against the running application, built in Docker, and
verified through API and browser checks. The author remains responsible for every submitted line and
should be prepared to explain the queue, prompt, validation, retry, scraping, and UI decisions.
The sanitized combined conversation export is in
[`docs/ai-assistant-log.md`](docs/ai-assistant-log.md).

See [`docs/SPEC.md`](docs/SPEC.md) for the complete design and [`CONTRACT.md`](CONTRACT.md) for the API contract.
