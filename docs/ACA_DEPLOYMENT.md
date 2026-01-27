# Payment Service - Azure Container Apps Deployment

## Overview

This guide covers deploying the Payment Service (.NET 8) to Azure Container Apps (ACA) with Stripe integration for payment processing.

## Prerequisites

- Azure CLI installed and authenticated
- Docker installed
- .NET 8 SDK installed
- Azure subscription with appropriate permissions
- Azure Container Registry (ACR) created
- Azure SQL Database
- Stripe account with API keys

## Quick Deployment

### Using the Deployment Script

**PowerShell (Windows):**

```powershell
cd scripts
.\aca.ps1
```

**Bash (macOS/Linux):**

```bash
cd scripts
./aca.sh
```

## Manual Deployment

### 1. Set Variables

```bash
RESOURCE_GROUP="rg-xshopai-aca"
LOCATION="swedencentral"
ACR_NAME="acrxshopaiaca"
ENVIRONMENT_NAME="cae-xshopai-aca"
SQL_SERVER="sql-xshopai-aca"
APP_NAME="payment-service"
APP_PORT=1009
DATABASE_NAME="payments_db"
```

### 2. Create Azure SQL Database

```bash
# Create SQL Server
az sql server create \
  --name $SQL_SERVER \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --admin-user sqladmin \
  --admin-password <password>

# Allow Azure services
az sql server firewall-rule create \
  --name AllowAzureServices \
  --server $SQL_SERVER \
  --resource-group $RESOURCE_GROUP \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0

# Create database
az sql db create \
  --name $DATABASE_NAME \
  --server $SQL_SERVER \
  --resource-group $RESOURCE_GROUP \
  --service-objective S0
```

### 3. Build and Push Image

```bash
# Publish .NET application
dotnet publish PaymentService/PaymentService.csproj -c Release -o ./publish

# Login to ACR
az acr login --name $ACR_NAME

# Build and push Docker image
docker build -t $ACR_NAME.azurecr.io/$APP_NAME:latest .
docker push $ACR_NAME.azurecr.io/$APP_NAME:latest
```

### 4. Deploy Container App

```bash
SQL_HOST="${SQL_SERVER}.database.windows.net"
CONNECTION_STRING="Server=tcp:${SQL_HOST},1433;Database=${DATABASE_NAME};User ID=sqladmin;Password=<password>;Encrypt=True;TrustServerCertificate=False;"

az containerapp create \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --environment $ENVIRONMENT_NAME \
  --image $ACR_NAME.azurecr.io/$APP_NAME:latest \
  --registry-server $ACR_NAME.azurecr.io \
  --target-port $APP_PORT \
  --ingress internal \
  --min-replicas 1 \
  --max-replicas 5 \
  --cpu 0.5 \
  --memory 1Gi \
  --enable-dapr \
  --dapr-app-id $APP_NAME \
  --dapr-app-port $APP_PORT \
  --secrets \
    "db-conn=$CONNECTION_STRING" \
    "stripe-key=<your-stripe-secret-key>" \
  --env-vars \
    "ASPNETCORE_URLS=http://+:$APP_PORT" \
    "ASPNETCORE_ENVIRONMENT=Production" \
    "ConnectionStrings__DefaultConnection=secretref:db-conn" \
    "Stripe__SecretKey=secretref:stripe-key"
```

## Configuration

### Environment Variables

| Variable                               | Description              |
| -------------------------------------- | ------------------------ |
| `ASPNETCORE_URLS`                      | ASP.NET Core URLs        |
| `ASPNETCORE_ENVIRONMENT`               | Environment (Production) |
| `ConnectionStrings__DefaultConnection` | SQL connection string    |
| `Stripe__SecretKey`                    | Stripe secret API key    |

## Security Considerations

1. Store Stripe keys as secrets only
2. Use managed identity when possible
3. Enable SSL for database connections
4. Use webhook signing for Stripe webhooks

## Monitoring

```bash
az containerapp logs show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --follow
```

## Troubleshooting

### Stripe Integration Issues

1. Verify API key is correct
2. Check webhook configuration
3. Review Stripe dashboard for errors

### Database Connection Issues

1. Verify SQL firewall rules
2. Check connection string format
3. Ensure encryption is enabled
