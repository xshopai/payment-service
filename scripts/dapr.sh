#!/bin/bash

# Payment Service - Run with Dapr Pub/Sub

echo "Starting Payment Service (Dapr Pub/Sub)..."
echo "Service will be available at: http://localhost:8009"
echo "Dapr HTTP endpoint: http://localhost:3509"
echo "Dapr gRPC endpoint: localhost:50009"
echo ""

# Navigate to service root directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVICE_DIR="$(dirname "$SCRIPT_DIR")"
cd "$SERVICE_DIR"

# Copy appsettings.Dapr.json to appsettings.json for Dapr mode
if [ -f "PaymentService/appsettings.Dapr.json" ]; then
    cp "PaymentService/appsettings.Dapr.json" "PaymentService/appsettings.json"
    echo "✅ Copied appsettings.Dapr.json → appsettings.json"
fi

# Kill any processes using required ports (prevents "address already in use" errors)
for PORT in 8009 3509 50009; do
    for pid in $(netstat -ano 2>/dev/null | grep ":$PORT" | grep LISTENING | awk '{print $5}' | sort -u); do
        echo "Killing process $pid on port $PORT..."
        taskkill //F //PID $pid 2>/dev/null
    done
done

dapr run \
  --app-id payment-service \
  --app-port 8009 \
  --dapr-http-port 3509 \
  --dapr-grpc-port 50009 \
  --log-level info \
  --config ./.dapr/config.yaml \
  --resources-path ./.dapr/components \
  -- dotnet run --project PaymentService/PaymentService.csproj --urls "http://localhost:8009"

