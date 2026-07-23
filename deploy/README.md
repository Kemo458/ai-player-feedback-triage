# Deployment

The reference deployment runs behind a Cloudflare Tunnel. No public application ports need to be
opened; Caddy is bound to `127.0.0.1` and the tunnel reaches it over the internal Docker network.
The repository contains no server hostname, SSH username, key path, or tunnel credentials.

## Topology
```
Internet → Cloudflare edge → cloudflared (tunnel) → caddy:80 →  ┌ /api,/hubs,/health,/swagger → api:8080
                                                                └ everything else            → web:80 (SPA)
                                          api → postgres, ollama (qwen2.5:3b), google-play-scraper
```

## First-time deploy
```bash
# Configure your own target. SSH_KEY is optional when your SSH agent/config supplies the key.
export SSH_HOST=deploy@example.com
export SSH_KEY=/path/to/private/key
export REMOTE_DIR=/opt/epam-player-feedback
export APP_HOSTNAME=feedback.example.com

# 1. Ensure the server has a production .env (see .env.ovh.example) at $REMOTE_DIR/.env.

# 2. Cloudflare tunnel (one-time)
cloudflared tunnel login
./deploy/setup-tunnel.sh

# 3. Sync code, rebuild, start services, and pull the configured model.
./deploy/deploy-ovh.sh
```

## Redeploy (code changes)
Export `SSH_HOST` and `REMOTE_DIR` as above, optionally export `SSH_KEY`, then run
`./deploy/deploy-ovh.sh`.

`deploy-ovh.sh` includes the `deploy/docker-compose.ovh.yml` overlay automatically when
`deploy/cloudflared/config.yml` exists, so the tunnel comes back up with the rest of the stack.

## Files
- `docker-compose.ovh.yml` — overlay adding the `cloudflared` service.
- `setup-tunnel.sh` — creates the named tunnel, routes DNS, copies the
  credentials JSON + generated `config.yml` to the server, starts the tunnel.
- `cloudflared/config.yml.template` — ingress template (`epam.valentingrozdev.space` → `caddy:80`).
- `cloudflared/<tunnel-id>.json` + `config.yml` — generated, git-ignored secrets (live on the server).
- `deploy-ovh.sh` — rsync + build + up + Ollama model pull + health check.
- `smoke.sh` — end-to-end API smoke test (`bash smoke.sh <base-url> <user> <pass>`).

## Operations
```bash
ssh "$SSH_HOST"
cd "$REMOTE_DIR"
docker compose -f docker-compose.yml -f deploy/docker-compose.ovh.yml ps
docker compose logs -f api            # worker + request logs
docker compose exec ollama ollama list
curl -fsS http://127.0.0.1:$(grep PUBLISH_PORT .env | cut -d= -f2)/health/dependencies  # needs auth
```

## Notes
- The tunnel credentials file must be world-readable (`chmod 644`) because the `cloudflared`
  container runs as a non-root user; it is still only reachable inside the private server account.
- Model: `qwen2.5:3b` (real Ollama tag). Change `LLM_MODEL` in `.env` and re-pull to swap models.
- Ollama is CPU-only here (`OLLAMA_CPUS=8`, `mem_limit=12g`, three parallel requests). The
  deployed `qwen2.5:3b` workload averages roughly 27 seconds/item; the durable queue + retries
  absorb the latency.
