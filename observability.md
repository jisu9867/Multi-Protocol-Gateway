# Observability Debug Guide

This document is a practical runbook for diagnosing Grafana `No data` issues and validating the fix with Playwright E2E.

## Scope

- Prometheus scrape from API `/metrics`
- Grafana provisioned datasource and dashboards
- Dashboard panel visibility and `No data` regression checks
- Automated E2E at `tests/e2e`

## Quick Start

1. Recreate stack:
   - `docker compose down -v`
   - `docker compose up -d --build`
2. Health checks:
   - API metrics: `http://localhost:5000/metrics`
   - Prometheus: `http://localhost:9090/-/healthy`
   - Grafana: `http://localhost:3000/api/health`
3. Run E2E:
   - `cd tests/e2e`
   - `npm.cmd test`

## Common Failure Patterns

### 1) `No data` in Grafana panels

Check:
- Prometheus target is `up` (`/api/v1/targets`)
- Dashboard query returns any vector
- Panel query has fallback (`or vector(0)`) where appropriate

Fix:
- Ensure dashboard JSON is provisioned and reloaded
- Restart Grafana container after query changes

### 2) `Dashboard not found` in browser/E2E

Check:
- Dashboard exists: `GET /api/search`
- Dashboard UID resolves: `GET /api/dashboards/uid/{uid}`
- User/anonymous permission includes dashboard read

Fix:
- Recreate stack (`down -v` then `up -d --build`)
- Verify Grafana auth settings in `docker-compose.yml`

### 3) E2E login/auth 403

Current test strategy:
- Try direct landing first
- Use Grafana session path only when needed
- Resolve dashboard URL via Grafana API (`meta.url`)

## E2E Contract

Test file:
- `tests/e2e/specs/grafana-observability.spec.js`

Expected behavior:
- Core dashboards load by UID
- Required panel headings are visible
- Dashboard body does not contain:
  - `No data`
  - `Query error`
  - `Data is missing`

## CI

Workflow:
- `.github/workflows/ci.yml` -> `observability-e2e` job

Pipeline actions:
- Start docker stack
- Wait for API/Prometheus/Grafana readiness
- Run Playwright tests
- Upload Playwright report artifacts

## Notes

- If local `npm test` is blocked by PowerShell policy, use `npm.cmd test`.
- If Grafana state looks stale, reset with `docker compose down -v`.
