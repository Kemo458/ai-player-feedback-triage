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

## Quick start (local)
```bash
cp .env.example .env        # a ready-to-run .env is already included for local dev
docker compose up -d --build
open http://localhost:8090
```
Sign in with `manager` / `manager-dev-pass` (from `.env`). A demo game **Royal Match (Demo)** is
seeded with sample feedback; the local `.env` uses the **Mock** analyzer, so classifications appear
within seconds with no model download.

Public feedback form for the demo game:
`http://localhost:8090/submit/<gameId>?token=demo-submission-token` (the gameId is shown in the games list).

### Using the real LLM locally
Set `LLM_PROVIDER=Ollama` in `.env`, then:
```bash
docker compose up -d
docker compose exec ollama ollama pull qwen2.5:3b
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

See [`docs/SPEC.md`](docs/SPEC.md) for the complete design and [`CONTRACT.md`](CONTRACT.md) for the API contract.
