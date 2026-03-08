# Multi-Protocol-Gateway

MQTT input is forwarded to Kafka, then consumed for PostgreSQL persistence and SignalR streaming.

- Core docs: [README.md](README.md), [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)
- Deployment docs: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)
- Observability runbook: [observability.md](observability.md)

## Dev Environment

- Start stack: `docker compose up --build`
- Endpoints:
  - API: `http://localhost:5000`
  - UI: `http://localhost:5001`
  - Prometheus: `http://localhost:9090`
  - Grafana: `http://localhost:3000`

## Testing Instructions

Use integration-first validation.

1. Start stack:
   - `docker compose up -d --build`
2. Run observability E2E:
   - `cd tests/e2e`
   - `npm.cmd test`
3. Validate:
   - Grafana dashboards load
   - Target panels do not show `No data`
   - SignalR/Prometheus pipeline stays healthy

## PR Notes

- Include integration verification evidence for behavior changes.
- Prefer Conventional Commit style (`feat:`, `fix:`, `chore:`).
