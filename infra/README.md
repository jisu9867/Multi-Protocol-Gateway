# Azure IaC (Bicep)

This folder is the single source of truth for Azure infrastructure.

## Structure
- `main.bicep`: Orchestrates all resources for one environment.
- `environments/dev.bicepparam`: Cost-optimized dev defaults.
- `environments/prod.bicepparam`: Production defaults.
- `modules/`: Reusable modules for each Azure subsystem.

## Target Architecture
- Azure Container Apps: `api`, `ui`, `mqtt`
- Azure Event Hubs (Kafka endpoint)
- Azure Database for PostgreSQL Flexible Server
- Azure Key Vault + Managed Identity
- Azure Monitor (Log Analytics + Application Insights)
- Azure Managed Grafana
- Azure Container Registry (Basic)

## Deployment
```bash
az deployment group what-if \
  --resource-group rg-gateway-dev-kr \
  --template-file infra/main.bicep \
  --parameters infra/environments/dev.bicepparam

az deployment group create \
  --resource-group rg-gateway-dev-kr \
  --template-file infra/main.bicep \
  --parameters infra/environments/dev.bicepparam
```

Repeat with `prod.bicepparam` and `rg-gateway-prod-kr` for production.
