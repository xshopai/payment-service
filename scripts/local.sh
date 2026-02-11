#!/bin/bash

# Payment Service - Run without Dapr (local development)

echo "Starting Payment Service (without Dapr)..."
echo "Service will be available at: http://localhost:8009"
echo ""
echo "Note: Event publishing and service-to-service calls will fail without Dapr."
echo "This mode is suitable for isolated development and testing."
echo ""

# Navigate to the service project directory
cd PaymentService

# Run with dotnet
dotnet run
