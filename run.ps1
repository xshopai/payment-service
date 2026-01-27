#!/usr/bin/env pwsh
# Payment Service - PowerShell Run Script with Dapr
# Port: 8009, Dapr HTTP: 3509, Dapr gRPC: 50009

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
#!/usr/bin/env pwsh
# Run Payment Service with Dapr sidecar
# Usage: .\run.ps1

$Host.UI.RawUI.WindowTitle = "Payment Service"

Write-Host "Starting Payment Service with Dapr..." -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Kill any existing processes on ports
Write-Host "Cleaning up existing processes..." -ForegroundColor Yellow

# Kill process on port 8009 (app port)
$process = Get-NetTCPConnection -LocalPort 8009 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique
if ($process) {
    Write-Host "Killing process on port 8009 (PID: $process)" -ForegroundColor Yellow
    Stop-Process -Id $process -Force -ErrorAction SilentlyContinue
}

# Kill process on port 3509 (Dapr HTTP port)
$process = Get-NetTCPConnection -LocalPort 3509 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique
if ($process) {
    Write-Host "Killing process on port 3509 (PID: $process)" -ForegroundColor Yellow
    Stop-Process -Id $process -Force -ErrorAction SilentlyContinue
}

# Kill process on port 50009 (Dapr gRPC port)
$process = Get-NetTCPConnection -LocalPort 50009 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique
if ($process) {
    Write-Host "Killing process on port 50009 (PID: $process)" -ForegroundColor Yellow
    Stop-Process -Id $process -Force -ErrorAction SilentlyContinue
}

Start-Sleep -Seconds 2

Write-Host ""
Write-Host "Starting with Dapr sidecar..." -ForegroundColor Green
Write-Host "App ID: payment-service" -ForegroundColor Cyan
Write-Host "App Port: 8009" -ForegroundColor Cyan
Write-Host "Dapr HTTP Port: 3509" -ForegroundColor Cyan
Write-Host "Dapr gRPC Port: 50009" -ForegroundColor Cyan
Write-Host ""

dapr run `
  --app-id payment-service `
  --app-port 8009 `
  --dapr-http-port 3509 `
  --dapr-grpc-port 50009 `
  --log-level error `
  --resources-path ./.dapr/components `
  --config ./.dapr/config.yaml `
  -- dotnet watch --project PaymentService/PaymentService.csproj

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Service stopped." -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
