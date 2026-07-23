#!/usr/bin/env bash
# Deploy the stack to the OVH server. Run from the repo root on your machine.
#   ./deploy/deploy-ovh.sh
set -euo pipefail

: "${SSH_HOST:?Set SSH_HOST to the remote SSH target (for example deploy@example.com)}"
: "${REMOTE_DIR:?Set REMOTE_DIR to the absolute deployment directory on the server}"

SSH_OPTIONS=(-o StrictHostKeyChecking=accept-new)
if [[ -n "${SSH_KEY:-}" ]]; then
  SSH_OPTIONS+=(-i "$SSH_KEY")
fi
RSYNC_SSH="$(printf '%q ' ssh "${SSH_OPTIONS[@]}")"

echo "==> Syncing project to ${SSH_HOST}:${REMOTE_DIR}"
rsync -az --delete \
  --exclude '.git' --exclude 'node_modules' --exclude 'bin' --exclude 'obj' \
  --exclude 'dist' --exclude '.venv' --exclude '__pycache__' \
  --exclude '.env' \
  -e "$RSYNC_SSH" \
  ./ "${SSH_HOST}:${REMOTE_DIR}/"

echo "==> Building & starting stack on the server"
REMOTE_DIR_ESCAPED="$(printf '%q' "$REMOTE_DIR")"
ssh "${SSH_OPTIONS[@]}" "$SSH_HOST" "REMOTE_DIR=${REMOTE_DIR_ESCAPED} bash -s" <<'REMOTE'
set -euo pipefail
cd "$REMOTE_DIR"
test -f .env || { echo "Missing $REMOTE_DIR/.env on server (copy deploy/.env.ovh.example)"; exit 1; }

FILES="-f docker-compose.yml"
if [ -f deploy/cloudflared/config.yml ]; then FILES="$FILES -f deploy/docker-compose.ovh.yml"; fi

docker compose $FILES up -d --build

echo '==> Pulling the Ollama model (first run only, may take a while)'
MODEL=$(grep -E '^LLM_MODEL=' .env | cut -d= -f2)
docker compose exec -T ollama ollama pull "${MODEL:-qwen2.5:3b}" || true

echo '==> Health:'
sleep 5
docker compose ps
curl -fsS http://127.0.0.1:$(grep -E '^PUBLISH_PORT=' .env | cut -d= -f2)/health/ready && echo || true
REMOTE

echo "==> Done."
