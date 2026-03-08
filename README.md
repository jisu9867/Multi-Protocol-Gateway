# Multi-Protocol-Gateway

Smart factory gateway that ingests MQTT telemetry, processes events, stores to PostgreSQL, and serves realtime UI updates.

## Runtime Architecture
- API: ASP.NET Core (`src/Gateway.Api`)
- UI: Blazor Server (`src/Gateway.Ui`)
- Messaging: Kafka-compatible endpoint (local Kafka or Azure Event Hubs Kafka)
- Storage: PostgreSQL / TimescaleDB
- Realtime: SignalR
- Observability: OpenTelemetry + Prometheus endpoint

## Local Docker
```bash
docker compose up --build
```

Endpoints:
- API: `http://localhost:5000`
- UI: `http://localhost:5001`
- PostgreSQL: `localhost:5433`
- MQTT broker: `localhost:1884`
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000`

## Configuration (Kafka)
`src/Gateway.Api/appsettings*.json` uses normalized Kafka settings:
- `Kafka__BootstrapServers`
- `Kafka__Topic`
- `Kafka__ConsumerGroupId`
- `Kafka__SecurityProtocol` (for Event Hubs: `SaslSsl`)
- `Kafka__SaslMechanism` (for Event Hubs: `Plain`)
- `Kafka__SaslUsername` (for Event Hubs: `$ConnectionString`)
- `Kafka__SaslPassword` (for Event Hubs: full connection string)

## Azure IaC
All Azure deployment resources are managed by Bicep under [`infra/`](infra/README.md):
- Container Apps (`api`, `ui`, `mqtt`)
- Event Hubs (Kafka endpoint)
- PostgreSQL Flexible Server
- Key Vault + Managed Identity
- Log Analytics + Application Insights + Managed Grafana
- ACR + Budget control

## CI/CD
- `ci.yml`: build and test
- `docker-build.yml`: container build pipeline
- `azure-iac-deploy.yml`: `what-if` -> image build/push -> deploy (dev/prod)
