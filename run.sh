#!/bin/bash
# Payment Service - Bash Run Script with Dapr
# Port: 8009, Dapr HTTP: 3509, Dapr gRPC: 50009

echo ""
echo "============================================"
echo "Starting payment-service with Dapr..."
echo "============================================"
echo ""

# Kill any existing processes on ports
echo "Cleaning up existing processes..."

# Kill processes on port 8009 (app port)
lsof -ti:8009 | xargs kill -9 2>/dev/null || true

# Kill processes on port 3509 (Dapr HTTP port)
lsof -ti:3509 | xargs kill -9 2>/dev/null || true

# Kill processes on port 50009 (Dapr gRPC port)
lsof -ti:50009 | xargs kill -9 2>/dev/null || true

sleep 2

echo ""
echo "Starting with Dapr sidecar..."
echo "App ID: payment-service"
echo "App Port: 8009"
echo "Dapr HTTP Port: 3509"
echo "Dapr gRPC Port: 50009"
echo ""

dapr run \
  --app-id payment-service \
  --app-port 8009 \
  --dapr-http-port 3509 \
  --dapr-grpc-port 50009 \
  --log-level error \
  --resources-path ./.dapr/components \
  --config ./.dapr/config.yaml \
  -- dotnet run --project PaymentService/PaymentService.csproj

echo ""
echo "============================================"
echo "Service stopped."
echo "============================================"
