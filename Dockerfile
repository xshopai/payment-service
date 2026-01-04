# =============================================================================
# Multi-stage Dockerfile for .NET Payment Service
# =============================================================================

# -----------------------------------------------------------------------------
# Base stage - Common setup for all stages
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# Install system dependencies
RUN apt-get update && apt-get install -y \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN groupadd -r paymentuser && useradd -r -g paymentuser paymentuser

# -----------------------------------------------------------------------------
# Build stage - Build the application
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies (better caching)
COPY ["PaymentService/PaymentService.csproj", "PaymentService/"]
RUN dotnet restore "PaymentService/PaymentService.csproj"

# Copy source code and build
COPY . .
RUN dotnet build "PaymentService/PaymentService.csproj" -c Release -o /app/build

# -----------------------------------------------------------------------------
# Publish stage - Publish the application
# -----------------------------------------------------------------------------
FROM build AS publish
RUN dotnet publish "PaymentService/PaymentService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# -----------------------------------------------------------------------------
# Development stage - For local development
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS development
WORKDIR /app

# Note: In development, mount code as volume for hot reload
# Install system dependencies (wget for healthcheck)
RUN apt-get update && apt-get install -y \
    wget \
    && rm -rf /var/lib/apt/lists/*

# Copy project file and restore dependencies
COPY ["PaymentService/PaymentService.csproj", "PaymentService/"]
RUN dotnet restore "PaymentService/PaymentService.csproj"

# Copy source code
COPY . .

# Create non-root user
RUN groupadd -r paymentuser && useradd -r -g paymentuser paymentuser
RUN chown -R paymentuser:paymentuser /app
USER paymentuser

# Health check (using wget which is smaller than curl)
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost/health || exit 1

# Expose port
EXPOSE 80
EXPOSE 443

# Run in development mode with hot reload
ENTRYPOINT ["dotnet", "watch", "run", "--project", "PaymentService/PaymentService.csproj", "--urls", "http://0.0.0.0:80"]

# -----------------------------------------------------------------------------
# Production stage - Optimized for production deployment
# -----------------------------------------------------------------------------
FROM base AS production

# Copy published app
COPY --from=publish --chown=paymentuser:paymentuser /app/publish .

# Remove unnecessary files for production
RUN rm -rf /tmp/* /var/tmp/*

# Switch to non-root user
USER paymentuser

# Health check (using wget which is smaller than curl)
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost/health || exit 1

# Expose port
EXPOSE 80
EXPOSE 443

# Configure ASP.NET Core
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

# Entry point
ENTRYPOINT ["dotnet", "PaymentService.dll"]

# Labels for better image management and security scanning
LABEL maintainer="xshop.ai Team"
LABEL service="payment-service"
LABEL version="1.0.0"
LABEL org.opencontainers.image.source="https://github.com/xshopai/xshopai"
LABEL org.opencontainers.image.description="Payment Service for xshop.ai platform"
LABEL org.opencontainers.image.vendor="xshop.ai"
LABEL framework="aspnetcore"
LABEL language="csharp"
