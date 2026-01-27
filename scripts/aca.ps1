# ============================================================================
# Azure Container Apps Deployment Script for Payment Service (PowerShell)
# ============================================================================

$ErrorActionPreference = "Stop"

function Write-Header { param([string]$Message); Write-Host "`n============================================================================" -ForegroundColor Blue; Write-Host $Message -ForegroundColor Blue; Write-Host "============================================================================`n" -ForegroundColor Blue }
function Write-Success { param([string]$Message); Write-Host "✓ $Message" -ForegroundColor Green }
function Write-Info { param([string]$Message); Write-Host "ℹ $Message" -ForegroundColor Blue }

function Read-HostWithDefault { param([string]$Prompt, [string]$Default); $input = Read-Host "$Prompt [$Default]"; if ([string]::IsNullOrWhiteSpace($input)) { return $Default }; return $input }

Write-Header "Checking Prerequisites"
try { az version | Out-Null; Write-Success "Azure CLI installed" } catch { Write-Error "Azure CLI not installed"; exit 1 }
try { docker version | Out-Null; Write-Success "Docker installed" } catch { Write-Error "Docker not installed"; exit 1 }
try { dotnet --version | Out-Null; Write-Success ".NET SDK installed" } catch { Write-Error ".NET SDK not installed"; exit 1 }
try { az account show | Out-Null } catch { az login }

Write-Header "Azure Configuration"
$ResourceGroup = Read-HostWithDefault -Prompt "Enter Resource Group name" -Default "rg-xshopai-aca"
$Location = Read-HostWithDefault -Prompt "Enter Azure Location" -Default "swedencentral"
$AcrName = Read-HostWithDefault -Prompt "Enter Azure Container Registry name" -Default "acrxshopaiaca"
$EnvironmentName = Read-HostWithDefault -Prompt "Enter Container Apps Environment name" -Default "cae-xshopai-aca"
$SqlServerName = Read-HostWithDefault -Prompt "Enter Azure SQL server name" -Default "sql-xshopai-aca"
$SqlPassword = Read-Host "Enter SQL admin password" -AsSecureString
$StripeSecretKey = Read-Host "Enter Stripe Secret Key" -AsSecureString

$AppName = "payment-service"
$AppPort = 1009
$DatabaseName = "payments_db"
$SqlPasswordPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlPassword))
$StripeKeyPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($StripeSecretKey))

$Confirm = Read-Host "Proceed with deployment? (y/N)"
if ($Confirm -notmatch '^[Yy]$') { exit 0 }

Write-Header "Setting Up Azure SQL Database"
try {
    az sql server show --name $SqlServerName --resource-group $ResourceGroup | Out-Null
    Write-Info "SQL Server exists"
} catch {
    az sql server create --name $SqlServerName --resource-group $ResourceGroup --location $Location --admin-user sqladmin --admin-password $SqlPasswordPlain --output none
    az sql server firewall-rule create --name AllowAzureServices --server $SqlServerName --resource-group $ResourceGroup --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --output none
    Write-Success "SQL Server created"
}

try {
    az sql db show --name $DatabaseName --server $SqlServerName --resource-group $ResourceGroup | Out-Null
    Write-Info "Database exists"
} catch {
    az sql db create --name $DatabaseName --server $SqlServerName --resource-group $ResourceGroup --service-objective S0 --output none
    Write-Success "Database created"
}

$SqlHost = "${SqlServerName}.database.windows.net"
$ConnectionString = "Server=tcp:${SqlHost},1433;Database=${DatabaseName};User ID=sqladmin;Password=${SqlPasswordPlain};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

Write-Header "Building and Deploying"
$AcrLoginServer = az acr show --name $AcrName --query loginServer -o tsv
az acr login --name $AcrName

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ServiceDir = Split-Path -Parent $ScriptDir
Push-Location $ServiceDir

try {
    dotnet publish PaymentService/PaymentService.csproj -c Release -o ./publish
    $ImageTag = "${AcrLoginServer}/${AppName}:latest"
    docker build -t $ImageTag .
    docker push $ImageTag
    Write-Success "Image pushed"
} finally { Pop-Location }

az containerapp env show --name $EnvironmentName --resource-group $ResourceGroup | Out-Null 2>$null
if ($LASTEXITCODE -ne 0) {
    az containerapp env create --name $EnvironmentName --resource-group $ResourceGroup --location $Location --output none
}

try {
    az containerapp show --name $AppName --resource-group $ResourceGroup | Out-Null
    az containerapp update --name $AppName --resource-group $ResourceGroup --image $ImageTag --output none
    Write-Success "Container app updated"
} catch {
    az containerapp create `
        --name $AppName `
        --resource-group $ResourceGroup `
        --environment $EnvironmentName `
        --image $ImageTag `
        --registry-server $AcrLoginServer `
        --target-port $AppPort `
        --ingress internal `
        --min-replicas 1 `
        --max-replicas 5 `
        --cpu 0.5 `
        --memory 1Gi `
        --enable-dapr `
        --dapr-app-id $AppName `
        --dapr-app-port $AppPort `
        --secrets "db-conn=$ConnectionString" "stripe-key=$StripeKeyPlain" `
        --env-vars `
            "ASPNETCORE_URLS=http://+:$AppPort" `
            "ASPNETCORE_ENVIRONMENT=Production" `
            "ConnectionStrings__DefaultConnection=secretref:db-conn" `
            "Stripe__SecretKey=secretref:stripe-key" `
        --output none
    Write-Success "Container app created"
}

Write-Header "Deployment Complete!"
Write-Host "Payment Service deployed! Dapr App ID: $AppName" -ForegroundColor Green
