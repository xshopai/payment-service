#!/bin/bash

# Payment Service - Run with direct RabbitMQ (local development)

echo "Starting Payment Service (Direct RabbitMQ)..."
echo "Service will be available at: http://localhost:8009"
echo ""

# Kill any process using port 8009 (prevents "address already in use" errors)
PORT=8009
for pid in $(netstat -ano 2>/dev/null | grep ":$PORT" | grep LISTENING | awk '{print $5}' | sort -u); do
    echo "Killing process $pid on port $PORT..."
    taskkill //F //PID $pid 2>/dev/null
done

# Navigate to service root directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVICE_DIR="$(dirname "$SCRIPT_DIR")"
cd "$SERVICE_DIR"

# Copy appsettings.Direct.json → appsettings.Development.json for local development
if [ -f "PaymentService/appsettings.Direct.json" ]; then
    cp "PaymentService/appsettings.Direct.json" "PaymentService/appsettings.Development.json"
    echo "✅ Copied appsettings.Direct.json → appsettings.Development.json"
fi

# Run with .NET (hot reload enabled)
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://+:8009
dotnet watch run --project PaymentService/PaymentService.csproj --no-launch-profile
