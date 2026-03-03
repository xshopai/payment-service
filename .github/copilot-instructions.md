# Copilot Instructions — payment-service

## Service Identity

- **Name**: payment-service
- **Purpose**: Payment processing — multi-provider support (Stripe, PayPal, Square), transaction lifecycle, refunds
- **Port**: 8009
- **Language**: C# 12 / .NET 8
- **Framework**: ASP.NET Core 8 Web API
- **Database**: SQL Server (port 1433) via Entity Framework Core 8
- **Dapr App ID**: `payment-service`

## Architecture

- **Pattern**: Provider abstraction — `IPaymentProvider` interface with Stripe, PayPal, Square, and Simulation implementations
- **API Style**: RESTful with Swagger/OpenAPI via Swashbuckle
- **Authentication**: JWT Bearer tokens
- **Messaging**: Dapr pub/sub for payment events (CloudEvents 1.0)
- **Event Format**: CloudEvents 1.0 specification

## Project Structure

```
payment-service/
├── PaymentService/
│   ├── Program.cs                    # Application bootstrap
│   ├── Controllers/                  # API endpoints
│   ├── Models/                       # Entity models + DTOs
│   ├── Data/                         # DbContext, entity configs
│   ├── Services/
│   │   ├── Providers/                # Payment provider implementations
│   │   │   ├── StripePaymentProvider.cs
│   │   │   ├── PayPalPaymentProvider.cs
│   │   │   ├── SquarePaymentProvider.cs
│   │   │   └── SimulationPaymentProvider.cs
│   │   └── PaymentService.cs         # Business logic orchestrator
│   ├── Messaging/                    # IMessagingProvider (Dapr + RabbitMQ)
│   ├── Events/Publishers/            # Event publishing
│   ├── Middlewares/                   # Correlation ID, logging
│   ├── Configuration/                # Settings classes
│   ├── Utils/                        # StandardLogger, helpers
│   └── Migrations/                   # EF Core migrations
├── .dapr/components/
└── PaymentService.csproj
```

## Code Conventions

- **C# 12** with nullable reference types enabled
- Use **Entity Framework Core 8** with SQL Server
- Use **FluentValidation** for request validation
- Use **Serilog** for structured logging
- Use **Dapr.AspNetCore** + **Dapr.Client** for pub/sub
- Provider pattern: `IPaymentProvider` interface, selected via configuration
- JSON serialization uses `System.Text.Json` with camelCase naming policy
- OpenTelemetry + Zipkin tracing + Azure Monitor integration
- Health checks with EF Core database health

## Key Patterns

- **Provider abstraction**: `IPaymentProvider` interface with `ProcessPaymentAsync`, `RefundPaymentAsync`, `GetPaymentStatusAsync`
- **SimulationPaymentProvider**: Default provider for development (no external API calls)
- **Stripe.net SDK** for Stripe integration
- **PayPalCheckoutSdk** for PayPal integration
- Payment statuses: `Pending` → `Processing` → `Completed` / `Failed` / `Refunded`
- Configuration-driven provider selection via `PaymentProviders` settings section

## Database Patterns

- SQL Server via EF Core
- Entities: `Payment`, `PaymentTransaction`, `Refund`
- Code-first migrations
- Connection string via `ConnectionStrings:DefaultConnection`
- Retry on transient failure enabled

## Testing Requirements

- All new controllers and services MUST have unit tests
- Use **xUnit** + **Moq** as the test framework
- Mock payment provider and messaging provider in unit tests
- Do NOT call real payment providers (Stripe, PayPal, Square) in unit tests — use SimulationPaymentProvider
- Do NOT call real SQL Server in unit tests
- Run: `dotnet test`

## Dapr Integration

- **Pub/Sub**: Publishes `payment.processed`, `payment.failed`, `payment.refunded` events
- **Secrets Store**: Optional Dapr secrets for provider API keys
- **Ports**: Dapr HTTP 3500, Dapr gRPC 50001

## Security Rules

- JWT Bearer token MUST be validated via ASP.NET Core Authentication before accessing any endpoint
- Payment provider API keys MUST be stored in Dapr secrets store or Azure Key Vault — never in appsettings or environment variables in plain text for production
- Validate all payment request bodies using **FluentValidation** before reaching service logic
- Sanitize all inputs
- Never expose payment provider secret keys, internal payment IDs, or raw provider responses in API responses
- SimulationPaymentProvider MUST be used in non-production environments (never call real providers in dev/test)

## Error Handling Contract

All errors MUST follow this JSON structure:

```json
{
  "error": {
    "code": "STRING_CODE",
    "message": "Human readable message",
    "correlationId": "uuid"
  }
}
```

- Never expose stack traces in production
- Use centralized exception middleware only
- Never expose raw payment provider error messages to API callers

## Logging Rules

- Use structured JSON logging via **Serilog**
- Include:
  - timestamp
  - level
  - serviceName
  - correlationId
  - message
- Never log JWT tokens
- Never log payment provider API keys or secrets
- Never log full payment card details

## Non-Goals

- This service does NOT manage orders — handled by order-service
- This service does NOT orchestrate the fulfillment saga — handled by order-processor-service
- This service does NOT manage product catalog or user profiles
- This service does NOT handle authentication or JWT issuance

## Environment Variables

```
PORT=8009
ASPNETCORE_ENVIRONMENT=Development
ConnectionStrings__DefaultConnection=Server=localhost,1433;Database=PaymentServiceDb;User Id=sa;Password=Admin123!;TrustServerCertificate=True
Jwt__Key=<shared-secret>
PaymentProviders__ActiveProvider=simulation
PaymentProviders__Stripe__SecretKey=sk_test_xxx
PaymentProviders__PayPal__ClientId=xxx
PaymentProviders__PayPal__ClientSecret=xxx
DAPR_HTTP_PORT=3500
```

## Common Commands

```bash
dotnet run --project PaymentService     # Run service
dotnet ef migrations add <Name>          # Add migration
dotnet ef database update                # Apply migrations
dotnet test                              # Run tests
dotnet build                             # Build project
```
