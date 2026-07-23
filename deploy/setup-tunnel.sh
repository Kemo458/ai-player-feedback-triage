#!/usr/bin/env bash
# Run AFTER `cloudflared tunnel login` (cert.pem present). Creates the tunnel + DNS and
# ships credentials to the server. Idempotent-ish: reuses the tunnel if it already exists.
set -euo pipefail

TUNNEL_NAME="${TUNNEL_NAME:-player-feedback}"
: "${APP_HOSTNAME:?Set APP_HOSTNAME to the public DNS name for the app}"
: "${SSH_HOST:?Set SSH_HOST to the remote SSH target (for example deploy@example.com)}"
: "${REMOTE_DIR:?Set REMOTE_DIR to the absolute deployment directory on the server}"
REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"

SSH_OPTIONS=(-o StrictHostKeyChecking=accept-new)
SCP_OPTIONS=(-o StrictHostKeyChecking=accept-new)
if [[ -n "${SSH_KEY:-}" ]]; then
  SSH_OPTIONS+=(-i "$SSH_KEY")
  SCP_OPTIONS+=(-i "$SSH_KEY")
fi

test -f "$HOME/.cloudflared/cert.pem" || { echo "cert.pem missing — run 'cloudflared tunnel login' first"; exit 1; }

echo "==> Ensuring tunnel '${TUNNEL_NAME}' exists"
if ! cloudflared tunnel list -o json | jq -e --arg n "$TUNNEL_NAME" '.[]|select(.name==$n)' >/dev/null; then
  cloudflared tunnel create "$TUNNEL_NAME"
fi
UUID=$(cloudflared tunnel list -o json | jq -r --arg n "$TUNNEL_NAME" '.[]|select(.name==$n)|.id')
echo "    tunnel id: $UUID"

echo "==> Routing DNS ${APP_HOSTNAME} -> tunnel"
cloudflared tunnel route dns "$TUNNEL_NAME" "$APP_HOSTNAME" || echo "    (DNS route may already exist)"

echo "==> Generating config.yml"
sed -e "s/__TUNNEL_ID__/${UUID}/g" -e "s/__APP_HOSTNAME__/${APP_HOSTNAME}/g" \
  "${REPO_DIR}/deploy/cloudflared/config.yml.template" > "${REPO_DIR}/deploy/cloudflared/config.yml"

echo "==> Shipping credentials to server"
REMOTE_DIR_ESCAPED="$(printf '%q' "$REMOTE_DIR")"
ssh "${SSH_OPTIONS[@]}" "$SSH_HOST" "mkdir -p ${REMOTE_DIR_ESCAPED}/deploy/cloudflared"
scp "${SCP_OPTIONS[@]}" \
  "$HOME/.cloudflared/${UUID}.json" "$SSH_HOST:${REMOTE_DIR}/deploy/cloudflared/${UUID}.json"
scp "${SCP_OPTIONS[@]}" \
  "${REPO_DIR}/deploy/cloudflared/config.yml" "$SSH_HOST:${REMOTE_DIR}/deploy/cloudflared/config.yml"

echo "==> Starting tunnel service on server"
ssh "${SSH_OPTIONS[@]}" "$SSH_HOST" \
  "cd ${REMOTE_DIR_ESCAPED} && docker compose -f docker-compose.yml -f deploy/docker-compose.ovh.yml up -d cloudflared"

echo "==> Done. https://${APP_HOSTNAME} should resolve shortly."
