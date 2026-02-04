#!/bin/bash

# Payment Service - Run with Dapr

echo "Starting Payment Service with Dapr..."
echo "Service will be available at: http://localhost:8009"
echo "Dapr HTTP endpoint: http://localhost:3509"
echo "Dapr gRPC endpoint: localhost:50009"
echo ""

dapr run \
  --app-id payment-service \
  --app-port 8009 \
  --dapr-http-port 3509 \
  --dapr-grpc-port 50009 \
  --log-level info \
  --config ./.dapr/config.yaml \
  --resources-path ./.dapr/components \
  -- dotnet run --project PaymentService/PaymentService.csproj --urls "http://localhost:8009" --environment Dapr

