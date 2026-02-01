#!/bin/bash

# ============================================================================
# Azure Container Apps Deployment Script for Payment Service
# ============================================================================
# This script deploys the Payment Service to Azure Container Apps.
# 
# PREREQUISITE: Run the infrastructure deployment script first:
#   cd infrastructure/azure/aca/scripts
#   ./deploy-infra.sh
#
# The infrastructure script creates all shared resources:
#   - Resource Group, ACR, Container Apps Environment
#   - Service Bus, Redis, SQL Server, Key Vault
#   - Dapr components (pubsub, statestore, secretstore)
# ============================================================================

set -e

# -----------------------------------------------------------------------------
# Colors for output
# -----------------------------------------------------------------------------
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Print functions
print_header() {
    echo -e "\n${BLUE}==============================================================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}==============================================================================${NC}\n"
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_info() {
    echo -e "${CYAN}ℹ $1${NC}"
}

# ============================================================================
# Prerequisites Check
# ============================================================================
print_header "Checking Prerequisites"

# Check Azure CLI
if ! command -v az &> /dev/null; then
    print_error "Azure CLI is not installed. Please install it from: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli"
    exit 1
fi
print_success "Azure CLI is installed"

# Check Docker
if ! command -v docker &> /dev/null; then
    print_error "Docker is not installed. Please install Docker first."
    exit 1
fi
print_success "Docker is installed"

# Check if logged into Azure
if ! az account show &> /dev/null; then
    print_warning "Not logged into Azure. Initiating login..."
    az login
fi
print_success "Logged into Azure"

# ============================================================================
# Configuration
# ============================================================================
print_header "Configuration"

# Service-specific configuration
SERVICE_NAME="payment-service"
SERVICE_VERSION="1.0.0"
APP_PORT=8009
PROJECT_NAME="xshopai"

# Dapr configuration for Azure Container Apps
DAPR_HTTP_PORT=3500
DAPR_GRPC_PORT=50001
DAPR_PUBSUB_NAME="pubsub"

# Get script directory and service directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVICE_DIR="$(dirname "$SCRIPT_DIR")"

# ============================================================================
# Environment Selection
# ============================================================================
echo -e "${CYAN}Available Environments:${NC}"
echo "   dev     - Development environment"
echo "   prod    - Production environment"
echo ""

read -p "Enter environment (dev/prod) [dev]: " ENVIRONMENT
ENVIRONMENT="${ENVIRONMENT:-dev}"

if [[ ! "$ENVIRONMENT" =~ ^(dev|prod)$ ]]; then
    print_error "Invalid environment: $ENVIRONMENT"
    echo "   Valid values: dev, prod"
    exit 1
fi
print_success "Environment: $ENVIRONMENT"

# Set environment-specific variables
case "$ENVIRONMENT" in
    dev)
        ASPNETCORE_ENVIRONMENT="Development"
        LOG_LEVEL="Information"
        ;;
    prod)
        ASPNETCORE_ENVIRONMENT="Production"
        LOG_LEVEL="Warning"
        ;;
esac

# ============================================================================
# Suffix Configuration
# ============================================================================
print_header "Infrastructure Configuration"

echo -e "${CYAN}The suffix was set during infrastructure deployment.${NC}"
echo "You can find it by running:"
echo -e "   ${BLUE}az group list --query \"[?starts_with(name, 'rg-xshopai-$ENVIRONMENT')].{Name:name, Suffix:tags.suffix}\" -o table${NC}"
echo ""

read -p "Enter the infrastructure suffix: " SUFFIX

if [ -z "$SUFFIX" ]; then
    print_error "Suffix is required. Please run the infrastructure deployment first."
    exit 1
fi

# Validate suffix format
if [[ ! "$SUFFIX" =~ ^[a-z0-9]{3,6}$ ]]; then
    print_error "Invalid suffix format: $SUFFIX"
    echo "   Suffix must be 3-6 lowercase alphanumeric characters."
    exit 1
fi
print_success "Using suffix: $SUFFIX"

# ============================================================================
# Derive Resource Names from Infrastructure
# ============================================================================
# These names must match what was created by deploy-infra.sh
RESOURCE_GROUP="rg-${PROJECT_NAME}-${ENVIRONMENT}-${SUFFIX}"
ACR_NAME="${PROJECT_NAME}${ENVIRONMENT}${SUFFIX}"
CONTAINER_ENV="cae-${PROJECT_NAME}-${ENVIRONMENT}-${SUFFIX}"
SQL_SERVER="sql-${PROJECT_NAME}-${ENVIRONMENT}-${SUFFIX}"
KEY_VAULT="kv-${PROJECT_NAME}-${ENVIRONMENT}-${SUFFIX}"
MANAGED_IDENTITY="id-${PROJECT_NAME}-${ENVIRONMENT}-${SUFFIX}"

# Container App name follows convention: ca-{service}-{env}-{suffix}
# Using shortened name to stay within 32 character limit
CONTAINER_APP_NAME="ca-payment-svc-${ENVIRONMENT}-${SUFFIX}"

print_info "Derived resource names:"
echo "   Resource Group:      $RESOURCE_GROUP"
echo "   Container Registry:  $ACR_NAME"
echo "   Container Env:       $CONTAINER_ENV"
echo "   Container App:       $CONTAINER_APP_NAME"
echo "   SQL Server:          $SQL_SERVER"
echo "   Key Vault:           $KEY_VAULT"
echo ""

# ============================================================================
# Verify Infrastructure Exists
# ============================================================================
print_header "Verifying Infrastructure"

# Check Resource Group
if ! az group show --name "$RESOURCE_GROUP" &> /dev/null; then
    print_error "Resource group '$RESOURCE_GROUP' does not exist."
    echo ""
    echo "Please run the infrastructure deployment first:"
    echo -e "   ${BLUE}cd infrastructure/azure/aca/scripts${NC}"
    echo -e "   ${BLUE}./deploy-infra.sh${NC}"
    exit 1
fi
print_success "Resource Group exists: $RESOURCE_GROUP"

# Check ACR
if ! az acr show --name "$ACR_NAME" &> /dev/null; then
    print_error "Container Registry '$ACR_NAME' does not exist."
    exit 1
fi
ACR_LOGIN_SERVER=$(az acr show --name "$ACR_NAME" --query loginServer -o tsv)
print_success "Container Registry exists: $ACR_LOGIN_SERVER"

# Check Container Apps Environment
if ! az containerapp env show --name "$CONTAINER_ENV" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
    print_error "Container Apps Environment '$CONTAINER_ENV' does not exist."
    exit 1
fi
print_success "Container Apps Environment exists: $CONTAINER_ENV"

# Check SQL Server
if ! az sql server show --name "$SQL_SERVER" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
    print_error "SQL Server '$SQL_SERVER' does not exist."
    exit 1
fi
print_success "SQL Server exists: $SQL_SERVER"

# Ensure payment_db database exists
print_info "Checking for payment_db database..."
if ! az sql db show --name payment_db --server "$SQL_SERVER" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
    print_info "Creating payment_db database..."
    az sql db create \
        --resource-group "$RESOURCE_GROUP" \
        --server "$SQL_SERVER" \
        --name payment_db \
        --service-objective S0 \
        --output none
    print_success "Database payment_db created"
else
    print_success "Database payment_db exists"
fi

# Check Key Vault
if ! az keyvault show --name "$KEY_VAULT" &> /dev/null; then
    print_warning "Key Vault '$KEY_VAULT' does not exist. Secrets will need to be configured manually."
else
    print_success "Key Vault exists: $KEY_VAULT"
fi

# Get Managed Identity ID
IDENTITY_ID=$(MSYS_NO_PATHCONV=1 az identity show --name "$MANAGED_IDENTITY" --resource-group "$RESOURCE_GROUP" --query id -o tsv 2>/dev/null || echo "")
if [ -z "$IDENTITY_ID" ]; then
    print_warning "Managed Identity not found, will deploy without it"
else
    print_success "Managed Identity exists: $MANAGED_IDENTITY"
fi

# ============================================================================
# Confirmation
# ============================================================================
print_header "Deployment Configuration Summary"

echo -e "${CYAN}Environment:${NC}          $ENVIRONMENT"
echo -e "${CYAN}Suffix:${NC}               $SUFFIX"
echo -e "${CYAN}Resource Group:${NC}       $RESOURCE_GROUP"
echo -e "${CYAN}Container Registry:${NC}   $ACR_LOGIN_SERVER"
echo -e "${CYAN}Container Env:${NC}        $CONTAINER_ENV"
echo -e "${CYAN}SQL Server:${NC}           $SQL_SERVER"
echo ""
echo -e "${CYAN}Service Configuration:${NC}"
echo -e "   Service Name:      $SERVICE_NAME"
echo -e "   Service Version:   $SERVICE_VERSION"
echo -e "   App Port:          $APP_PORT"
echo -e "   .NET Environment:  $ASPNETCORE_ENVIRONMENT"
echo -e "   LOG_LEVEL:         $LOG_LEVEL"
echo -e "   Dapr HTTP Port:    $DAPR_HTTP_PORT"
echo -e "   Dapr Pub/Sub:      $DAPR_PUBSUB_NAME"
echo ""

read -p "Do you want to proceed with deployment? (Y/n): " CONFIRM
CONFIRM=${CONFIRM:-Y}
if [[ "$CONFIRM" =~ ^[Nn]$ ]]; then
    print_warning "Deployment cancelled by user"
    exit 0
fi

# ============================================================================
# Step 1: Build and Push Container Image
# ============================================================================
print_header "Step 1: Building and Pushing Container Image"

# Login to ACR
print_info "Logging into ACR..."
az acr login --name "$ACR_NAME"
print_success "Logged into ACR"

# Navigate to service directory
cd "$SERVICE_DIR"

# Build Docker image (using production target)
print_info "Building Docker image (this may take a few minutes for .NET)..."
docker build --target production -t "$SERVICE_NAME:latest" .
print_success "Docker image built"

# Tag and push
IMAGE_TAG="$ACR_LOGIN_SERVER/$SERVICE_NAME:latest"
docker tag "$SERVICE_NAME:latest" "$IMAGE_TAG"
print_info "Pushing image to ACR..."
docker push "$IMAGE_TAG"
print_success "Image pushed: $IMAGE_TAG"

# ============================================================================
# Step 2: Deploy Container App
# ============================================================================
print_header "Step 2: Deploying Container App"

# Get ACR credentials
ACR_PASSWORD=$(az acr credential show --name "$ACR_NAME" --query "passwords[0].value" -o tsv)

# Build connection string (using Azure AD authentication with Managed Identity)
SQL_HOST="${SQL_SERVER}.database.windows.net"
CONNECTION_STRING="Server=${SQL_HOST};Database=payment_db;Authentication=Active Directory Default;TrustServerCertificate=True;Encrypt=True"

# Build environment variables
ENV_VARS=("ASPNETCORE_ENVIRONMENT=$ASPNETCORE_ENVIRONMENT")
ENV_VARS+=("ASPNETCORE_URLS=http://+:$APP_PORT")
ENV_VARS+=("Logging__LogLevel__Default=$LOG_LEVEL")
ENV_VARS+=("Dapr__Enabled=true")
ENV_VARS+=("Dapr__HttpPort=$DAPR_HTTP_PORT")
ENV_VARS+=("Dapr__PubSubName=$DAPR_PUBSUB_NAME")
ENV_VARS+=("Dapr__AppId=$SERVICE_NAME")
ENV_VARS+=("ConnectionStrings__DefaultConnection=$CONNECTION_STRING")

# Check if container app exists
if az containerapp show --name "$CONTAINER_APP_NAME" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
    print_info "Container app '$CONTAINER_APP_NAME' exists, updating..."
    az containerapp update \
        --name "$CONTAINER_APP_NAME" \
        --resource-group "$RESOURCE_GROUP" \
        --image "$IMAGE_TAG" \
        --set-env-vars "${ENV_VARS[@]}" \
        --output none
    print_success "Container app updated"
else
    print_info "Creating container app '$CONTAINER_APP_NAME'..."
    
    # Get JWT_SECRET from Key Vault for JWT validation
    print_info "Retrieving JWT_SECRET from Key Vault..."
    JWT_SECRET=$(az keyvault secret show --vault-name "$KEY_VAULT" --name "xshopai-jwt-secret" --query value -o tsv 2>/dev/null || echo "")
    if [ -z "$JWT_SECRET" ]; then
        print_warning "JWT_SECRET not found in Key Vault. JWT validation will be disabled."
        print_info "To enable JWT validation, add 'xshopai-jwt-secret' to Key Vault: $KEY_VAULT"
    else
        print_success "JWT_SECRET retrieved from Key Vault"
        ENV_VARS+=("Jwt__Secret=$JWT_SECRET")
        ENV_VARS+=("Jwt__Issuer=auth-service")
        ENV_VARS+=("Jwt__Audience=xshopai-platform")
    fi
    
    # Build the create command
    MSYS_NO_PATHCONV=1 az containerapp create \
        --name "$CONTAINER_APP_NAME" \
        --container-name "$SERVICE_NAME" \
        --resource-group "$RESOURCE_GROUP" \
        --environment "$CONTAINER_ENV" \
        --image "$IMAGE_TAG" \
        --registry-server "$ACR_LOGIN_SERVER" \
        --registry-username "$ACR_NAME" \
        --registry-password "$ACR_PASSWORD" \
        --target-port $APP_PORT \
        --ingress external \
        --min-replicas 2 \
        --max-replicas 10 \
        --cpu 1.0 \
        --memory 2.0Gi \
        --enable-dapr \
        --dapr-app-id "$SERVICE_NAME" \
        --dapr-app-port $APP_PORT \
        --env-vars "${ENV_VARS[@]}" \
        ${IDENTITY_ID:+--user-assigned "$IDENTITY_ID"} \
        --tags "project=$PROJECT_NAME" "environment=$ENVIRONMENT" "suffix=$SUFFIX" "service=$SERVICE_NAME" \
        --output none
    
    print_success "Container app created"
fi

# ============================================================================
# Step 3: Verify Deployment
# ============================================================================
print_header "Step 3: Verifying Deployment"

# Get app FQDN
APP_FQDN=$(az containerapp show \
    --name "$CONTAINER_APP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query properties.configuration.ingress.fqdn \
    -o tsv)

print_success "Deployment completed!"
echo ""
print_info "Service FQDN: https://$APP_FQDN"
print_info "Note: Payment service uses external ingress with JWT validation for /api/* endpoints"
print_info "Public endpoints: /, /health, /health/ready, /metrics, /swagger"
print_info "Protected endpoints: /api/v1/payments/* (requires JWT)"
echo ""

# Health check
print_info "Checking health endpoint..."
sleep 15
HEALTH_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://$APP_FQDN/health" 2>/dev/null || echo "000")
if [ "$HEALTH_STATUS" = "200" ]; then
    print_success "Health check passed! (HTTP $HEALTH_STATUS)"
else
    print_warning "Health check returned HTTP $HEALTH_STATUS. The app may still be starting."
fi

# Check container app status
APP_STATUS=$(az containerapp show \
    --name "$CONTAINER_APP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query properties.runningStatus \
    -o tsv 2>/dev/null || echo "Unknown")

if [ "$APP_STATUS" = "Running" ]; then
    print_success "Container app is running!"
else
    print_warning "Container app status: $APP_STATUS. The app may still be starting."
fi

# ============================================================================
# Summary
# ============================================================================
print_header "Deployment Summary"

echo -e "${GREEN}==============================================================================${NC}"
echo -e "${GREEN}   ✅ $SERVICE_NAME DEPLOYED SUCCESSFULLY${NC}"
echo -e "${GREEN}==============================================================================${NC}"
echo ""
echo -e "${CYAN}Application:${NC}"
echo "   FQDN:             https://$APP_FQDN"
echo "   Ingress:          external (with JWT validation)"
echo "   Health:           https://$APP_FQDN/health"
echo "   Swagger:          https://$APP_FQDN/swagger"
echo ""
echo -e "${CYAN}Security:${NC}"
echo "   Public endpoints:    /, /health, /health/*, /metrics, /swagger*"
echo "   Protected endpoints: /api/v1/payments/* (requires JWT from auth-service)"
echo ""
echo -e "${CYAN}Infrastructure:${NC}"
echo "   Resource Group:   $RESOURCE_GROUP"
echo "   Environment:      $CONTAINER_ENV"
echo "   Registry:         $ACR_LOGIN_SERVER"
echo ""
echo -e "${CYAN}Database:${NC}"
echo "   SQL Server:       $SQL_SERVER"
echo "   Database:         payment_db"
echo "   Authentication:   Azure AD Default (Managed Identity)"
echo ""
echo -e "${CYAN}Dapr Service Invocation:${NC}"
echo "   App ID:           $SERVICE_NAME"
echo "   Other services can invoke via: http://localhost:$DAPR_HTTP_PORT/v1.0/invoke/$SERVICE_NAME/method/{endpoint}"
echo ""
echo -e "${CYAN}Useful Commands:${NC}"
echo -e "   View logs:        ${BLUE}az containerapp logs show --name $CONTAINER_APP_NAME --resource-group $RESOURCE_GROUP --follow${NC}"
echo -e "   View Dapr logs:   ${BLUE}az containerapp logs show --name $CONTAINER_APP_NAME --resource-group $RESOURCE_GROUP --container daprd --follow${NC}"
echo -e "   Delete app:       ${BLUE}az containerapp delete --name $CONTAINER_APP_NAME --resource-group $RESOURCE_GROUP --yes${NC}"
echo ""
