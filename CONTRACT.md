# API & Service Contracts

The backend (ASP.NET Core) serves on container port `8080`. In the deployed stack, a Caddy
reverse proxy fronts everything on one origin: `/api/*`, `/hubs/*`, `/health/*`, `/swagger*` go
to the API; everything else goes to the SPA. So **the frontend calls same-origin relative URLs**
(`/api/...`, `/hubs/feedback`). No CORS needed in the deployed stack (dev vite proxy handles local).

All API errors use RFC7807 `application/problem+json`. Dates are UTC ISO-8601. IDs are UUID strings.

## Auth
- `POST /api/auth/login` body `{ "username": string, "password": string }` -> `200 { "token": string, "expiresAt": iso }`.
- Manager endpoints require header `Authorization: Bearer <token>`.
- Public feedback submission requires header `X-Submission-Token: <token>` (per game), no bearer.

## Games (manager)
- `GET /api/games?cursor=&limit=25` -> `{ "items": Game[], "nextCursor": string|null }`
- `POST /api/games` body `{ "name": string, "googlePlayUrl"?: string }` -> `201 Game`
- `GET /api/games/{gameId}` -> `Game`
- `POST /api/games/{gameId}/submission-token` -> `200 Game`
  - Compatibility endpoint that retrieves the same canonical token; it never rotates the link.
- `Game` = `{ id, name, googlePlayUrl|null, googlePlayPackageId|null, iconUrl|null,
  submissionEnabled, submissionToken|null, createdAt }`
  - `iconUrl` is resolved from cached Google Play public metadata for manager list responses.
  - `submissionToken` is returned on create, single-game `GET`, and the compatibility endpoint.
    It is deterministic per game and signing key, so each game has one permanent shareable link.
    Collection `GET /api/games` responses omit it.

## Google Play import (manager)
- `POST /api/games/{gameId}/imports/google-play` body:
  `{ "url": string, "count": 1..500 =100, "language"="en", "country"="us", "sort":"newest"|"mostRelevant"="newest", "score": null|1..5 }`
  -> `202 { id, status, requestedCount, createdAt }`, `Location: /api/imports/{id}`
- `GET /api/imports/{importId}` -> `ImportJob`
- `POST /api/imports/{importId}/cancel` -> `202`
- `ImportJob` = `{ id, gameId, status, requestedCount, fetchedCount, insertedCount, updatedCount, skippedCount, failedCount, lastErrorCode|null, lastErrorMessage|null, createdAt, startedAt|null, completedAt|null }`
  - status ∈ `Queued|Fetching|Persisting|Completed|PartiallyCompleted|Failed|Cancelled`

## Internal feedback (public)
- Shareable form: `/submit/{gameId}?token={submissionToken}`. A game has one canonical link that
  remains identical across reloads and repeated manager retrievals.
- `POST /api/public/games/{gameId}/feedback` header `X-Submission-Token`, body:
  `{ "text": string(3..5000), "rating"?: 1..5, "appVersion"?: string, "device"?: string, "locale"?: string }`
  -> `202 { id, status }`, `Location: /api/public/feedback/{id}/status`
- `GET /api/public/feedback/{feedbackId}/status` -> `{ id, status }` (coarse only)

## Feedback (manager)
- `GET /api/games/{gameId}/feedback?source=&tag=&severity=&sentiment=&status=&entity=&search=&sort=&cursor=&limit=50`
  -> `{ "items": Feedback[], "nextCursor": string|null }`
  - source ∈ `GooglePlay|Internal` (omit = all)
  - tag ∈ `Bug|Feature|Lore|Toxic`; severity ∈ `Critical|High|Medium|Low|Unknown`
  - sentiment ∈ `Positive|Neutral|Negative|Mixed`; status ∈ processing states or the virtual
    `Active` value (`Pending|Processing|RetryScheduled`)
  - sort ∈ `-sourceCreatedAt` (default/newest), `-importedAt`, `priority`, `-rating`,
    `rating`, `confidence`
- `GET /api/feedback/{feedbackId}` -> `Feedback`
- `POST /api/feedback/{feedbackId}/retry` -> `202` (409 if already processing)
- `POST /api/feedback/{feedbackId}/mark-reviewed` -> `200`
- `Feedback` = `{ id, gameId, source, externalId|null, text, rating|null, appVersion|null, device|null,
   sourceCreatedAt|null, importedAt, status, attemptCount, lastErrorCode|null, lastErrorMessage|null,
   analysis: Analysis|null }`
- `Analysis` = `{ primaryCategory, tags: string[], severity, toxicity, sentiment, summary, confidence:0..1,
   requiresManualReview: bool, provider, model, createdAt, entities: Entity[] }`
- `Entity` = `{ type, name, normalizedName, evidence, confidence:0..1 }`
- Processing status ∈ `Pending|Processing|Completed|RetryScheduled|ManualReview|Failed`

## Dashboard / summaries / entities (manager)
- `GET /api/games/{gameId}/dashboard` -> `Dashboard`
- `Dashboard` = `{ totals:{ total, bySource:{GooglePlay,Internal} },
   processing:{ pending, processing, completed, retryScheduled, manualReview, failed },
   categories:{ Bug,Feature,Lore,Toxic,Other }, severities:{...}, sentiments:{...},
   criticalBugs, toxic, failuresAndManualReview,
   topEntities: {normalizedName,type,count}[], ratingDistribution:{ "1":n,...,"5":n } }`
- `GET /api/games/{gameId}/summaries?source=&tag=&severity=` -> `Summary`
- `POST /api/games/{gameId}/summaries/refresh?source=&tag=&severity=` -> `202`
- `Summary` = `{ id, status, overview, themes: {name,count}[], includedFeedbackCount,
  generatedAt|null, invalidatedAt|null, provider, model }`
  - status ∈ `Pending|Generating|Ready|Invalidated|Empty|Failed`
- `GET /api/games/{gameId}/entities?type=&source=` -> `{ items: {normalizedName,type,mentionCount,feedbackCount,sourceBreakdown,sentimentBreakdown,evidence:string[]}[] }`

## SignalR
- Hub `/hubs/feedback`. Managers connect with `?access_token=<jwt>` (SignalR sends bearer via query for ws).
- Client invokes `JoinGame(gameId)` to join a game group.
- Server sends method `notify` with payload `{ eventType, gameId, feedbackId?|importId?|summaryId? }`.
  - eventType ∈ `FeedbackCompleted|ImportProgressChanged|SummaryUpdated`
- On receipt, invalidate matching TanStack Query keys and refetch. Refetch fully on reconnect.

## Health
- `GET /health/live` (process only), `GET /health/ready` (db+config), `GET /health/dependencies`.

## Scraper internal contract (API -> google-play-scraper)
`POST /internal/v1/google-play/reviews` header `X-Internal-Service-Key: <secret>` body:
`{ packageId, count, language, country, sort:"newest"|"mostRelevant", score: null|1..5 }`
-> `{ packageId, requestedCount, returnedCount, reviews: [
   { externalId, text, rating|null, thumbsUpCount, appVersion|null, createdAt(iso)|null,
     developerReply|null, developerRepliedAt|null } ] }`
Also `GET /health` -> `{status:"ok"}`.

- `GET /internal/v1/google-play/apps/{packageId}` with `X-Internal-Service-Key`
  -> `{ packageId, title|null, iconUrl|null }`
