#!/usr/bin/env bash
set -euo pipefail
B="${1:-http://localhost:8090}"
USER="${2:-manager}"
PASS="${3:-manager-dev-pass}"

echo "=== /health/ready ==="; curl -fsS "$B/health/ready"; echo
TOKEN=$(curl -fsS -X POST "$B/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"username\":\"$USER\",\"password\":\"$PASS\"}" | jq -r .token)
echo "token acquired: ${#TOKEN} chars"
AUTH=(-H "Authorization: Bearer $TOKEN")

GID=$(curl -fsS "$B/api/games" "${AUTH[@]}" | jq -r '.items[0].id')
echo "gameId=$GID"

echo "=== dashboard ==="
curl -fsS "$B/api/games/$GID/dashboard" "${AUTH[@]}" \
  | jq '{total:.totals.total, processing:.processing, criticalBugs, toxic, categories, severities, sentiments, topEntities:[.topEntities[]?|{n:.normalizedName,t:.type,c:.count}]}'

echo "=== sample analyzed feedback ==="
curl -fsS "$B/api/games/$GID/feedback?limit=4" "${AUTH[@]}" \
  | jq '.items[] | {status, primary:.analysis.primaryCategory, sev:.analysis.severity, tox:.analysis.toxicity, sent:.analysis.sentiment, conf:.analysis.confidence, summary:.analysis.summary, entities:[.analysis.entities[]?.name]}'

echo "=== critical bugs view ==="
curl -fsS "$B/api/games/$GID/feedback?tag=Bug&severity=Critical" "${AUTH[@]}" | jq '{count:(.items|length), first:.items[0].text}'

echo "=== toxic view ==="
curl -fsS "$B/api/games/$GID/feedback?tag=Toxic" "${AUTH[@]}" | jq '{count:(.items|length)}'

echo "=== public internal submission ==="
SUB=$(curl -fsS -X POST "$B/api/public/games/$GID/feedback" \
  -H 'Content-Type: application/json' -H 'X-Submission-Token: demo-submission-token' \
  -d '{"text":"The boss fight in the Frozen Keep crashes my iPhone 14 on version 2.1.0 every time.","rating":1,"device":"iPhone 14","appVersion":"2.1.0"}')
echo "$SUB" | jq '{id, status}"'  2>/dev/null || echo "$SUB"
FID=$(echo "$SUB" | jq -r '.id')
echo "submitted feedbackId=$FID"

echo "=== wait for async analysis, then read it back ==="
for i in $(seq 1 15); do
  ST=$(curl -fsS "$B/api/feedback/$FID" "${AUTH[@]}" | jq -r '.status')
  if [ "$ST" = "Completed" ] || [ "$ST" = "ManualReview" ]; then break; fi
  sleep 1
done
curl -fsS "$B/api/feedback/$FID" "${AUTH[@]}" \
  | jq '{status, summary:.analysis.summary, tags:.analysis.tags, severity:.analysis.severity, entities:[.analysis.entities[]?|{type,name}]}'

echo "=== summary (mock) for all sources ==="
curl -fsS -X POST "$B/api/games/$GID/summaries/refresh" "${AUTH[@]}" >/dev/null
for i in $(seq 1 15); do
  S=$(curl -fsS "$B/api/games/$GID/summaries" "${AUTH[@]}")
  ST=$(echo "$S" | jq -r '.status')
  if [ "$ST" = "Ready" ] || [ "$ST" = "Empty" ]; then break; fi
  sleep 1
done
echo "$S" | jq '{status, includedFeedbackCount, overview, themes:[.themes[]?|{name,count}]}'

echo "=== ALL SMOKE CHECKS PASSED ==="
