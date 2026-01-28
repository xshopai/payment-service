# 💳 Payment Service

Payment processing microservice for xshopai - handles payment transactions, refunds, and payment methods across multiple providers (Stripe, PayPal, Square).

## 🚀 Quick Start

### Prerequisites

- **.NET 8 SDK** ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **SQL Server** 2019+ ([Download](https://www.microsoft.com/sql-server/sql-server-downloads))
- **Dapr CLI** 1.16+ ([Install Guide](https://docs.dapr.io/getting-started/install-dapr-cli/))

### Setup

**1. Start SQL Server**

```bash
# Using Docker (recommended)
docker run -d --name payment-sqlserver -p 1433:1433 \
  -e 'ACCEPT_EULA=Y' \
  -e 'SA_PASSWORD=YourStrong@Passw0rd' \
  mcr.microsoft.com/mssql/server:2019-latest

# Or install SQL Server locally
```

**2. Clone & Restore**

```bash
git clone https://github.com/xshopai/payment-service.git
cd payment-service
dotnet restore
```

**3. Configure Environment**

```bash
# Copy environment template
cp .env.example .env

# Edit .env - update these values:
# ConnectionStrings__DefaultConnection=Server=localhost,1433;Database=payment_db;User Id=sa;Password=YourStrong@Passw0rd
# Stripe__SecretKey=your_stripe_secret_key
# Stripe__PublishableKey=your_stripe_publishable_key
```

**4. Apply Migrations**

```bash
dotnet ef database update
```

**5. Run Service**

```bash
# Start with Dapr (recommended)
./run.sh       # Linux/Mac
.\run.ps1      # Windows

# Or run directly
dotnet run
```

**6. Verify**

```bash
# Check health
curl http://localhost:8080/health

# Swagger UI
Open http://localhost:8080/swagger
```

### Common Commands

```bash
# Run tests
dotnet test

# Build
dotnet build

# Apply new migration
dotnet ef migrations add MigrationName

# Production mode
dotnet run --configuration Release
```

## 📚 Documentation

| Document                                      | Description                             |
| --------------------------------------------- | --------------------------------------- |
| [📖 Developer Guide](docs/DEVELOPER_GUIDE.md) | Local setup, debugging, daily workflows |
| [📘 Technical Reference](docs/TECHNICAL.md)   | Architecture, security, monitoring      |
| [🤝 Contributing](docs/CONTRIBUTING.md)       | Contribution guidelines and workflow    |

**API Documentation**: Swagger UI available at `/swagger` endpoint.

## ⚙️ Configuration

### Required Environment Variables

```bash
# Service
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:8080

# Database
ConnectionStrings__DefaultConnection=Server=localhost,1433;Database=payment_db;User Id=sa;Password=YourStrong@Passw0rd

# JWT
Jwt__Key=your-secret-key-min-32-characters
Jwt__Issuer=PaymentService
Jwt__Audience=PaymentService.Users

# Payment Providers
Stripe__SecretKey=sk_test_...
Stripe__PublishableKey=pk_test_...
PayPal__ClientId=your_paypal_client_id
PayPal__ClientSecret=your_paypal_client_secret

# Dapr
DAPR_HTTP_PORT=3500
DAPR_GRPC_PORT=50001
```

See [.env.example](.env.example) for complete configuration options.

## ✨ Key Features

- Multi-provider support (Stripe, PayPal, Square)
- Payment processing with provider abstraction
- Refund management (full and partial)
- Payment method storage and retrieval
- Transaction history and audit trails
- Multi-currency support (USD, EUR, GBP, CAD)
- PCI compliance considerations
- JWT authentication
- Idempotency for payment operations

## API Endpoints

### Payments

```http
POST   /api/payments              # Process a payment
GET    /api/payments              # Get payments with filtering
GET    /api/payments/{id}         # Get specific payment
POST   /api/payments/{id}/refund  # Process refund
GET    /api/payments/order/{orderId} # Get payment by order ID
```

### Payment Methods

```http
POST   /api/paymentmethods        # Save payment method
GET    /api/paymentmethods/customer/{customerId} # Get customer's payment methods
DELETE /api/paymentmethods/{id}   # Delete payment method
GET    /api/paymentmethods/supported-methods # Get supported payment methods
GET    /api/paymentmethods/providers # Get available providers
GET    /api/paymentmethods/providers/{name}/status # Check provider status
```

## Configuration

### appsettings.json Structure

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=PaymentServiceDb;..."
  },
  "Jwt": {
    "Key": "your-jwt-key",
    "Issuer": "xshopai-payment-service",
    "Audience": "xshopai-clients"
  },
  "PaymentService": {
    "MaxPaymentAmount": 10000.0,
    "DefaultCurrency": "USD",
    "AllowedCurrencies": ["USD", "EUR", "GBP", "CAD"]
  },
  "PaymentProviders": {
    "DefaultProvider": "stripe",
    "Stripe": {
      "IsEnabled": true,
      "PublishableKey": "pk_test_...",
      "SecretKey": "sk_test_...",
      "WebhookSecret": "whsec_...",
      "SupportedMethods": ["visa", "mastercard", "amex"]
    },
    "PayPal": {
      "IsEnabled": true,
      "ClientId": "your_paypal_client_id",
      "ClientSecret": "your_paypal_client_secret",
      "IsSandbox": true,
      "ReturnUrl": "https://localhost:7000/payment/success",
      "CancelUrl": "https://localhost:7000/payment/cancelled"
    },
    "Square": {
      "IsEnabled": false,
      "ApplicationId": "your_square_app_id",
      "AccessToken": "your_square_access_token",
      "IsSandbox": true
    }
  }
}
```

## Getting Started

### Prerequisites

- .NET Core 8 SDK
- SQL Server (LocalDB for development)
- Payment provider accounts (Stripe, PayPal, Square)

### Setup

1. **Clone and Navigate**

   ```bash
   cd payment-service
   ```

2. **Configure Connection String**
   Update `appsettings.json` with your SQL Server connection string.

3. **Configure Payment Providers**
   Add your payment provider credentials to `appsettings.json` or `appsettings.Development.json`.

4. **Install Dependencies**

   ```bash
   dotnet restore
   ```

5. **Run Database Migrations**

   ```bash
   dotnet ef database update
   ```

6. **Run the Service**

   ```bash
   dotnet run
   ```

7. **Access Swagger UI**
   Navigate to `https://localhost:7001` for API documentation.

## Database Schema

### Core Tables

- **Payments**: Main payment records with provider transaction IDs
- **PaymentRefunds**: Refund records linked to original payments
- **PaymentMethods**: Stored customer payment methods with provider tokens

### Key Features

- **Audit Fields**: CreatedAt, UpdatedAt, CreatedBy, UpdatedBy on all entities
- **Metadata Storage**: JSON columns for provider-specific data
- **Proper Indexing**: Performance optimized queries
- **Money Data Type**: Precise financial calculations

## Payment Flow Examples

### Process Payment

```json
POST /api/payments
{
  "orderId": "ORD-12345",
  "customerId": "CUST-67890",
  "amount": 99.99,
  "currency": "USD",
  "paymentMethod": "visa",
  "paymentProvider": "stripe",
  "description": "Order #12345",
  "paymentMethodDetails": {
    "card": {
      "number": "4242424242424242",
      "expiryMonth": 12,
      "expiryYear": 2025,
      "cvc": "123",
      "holderName": "John Doe"
    }
  }
}
```

### Process Refund

```json
POST /api/payments/123/refund
{
  "amount": 49.99,
  "reason": "Customer requested partial refund"
}
```

### Save Payment Method

```json
POST /api/paymentmethods
{
  "customerId": "CUST-67890",
  "paymentProvider": "stripe",
  "paymentMethodType": "card",
  "isDefault": true,
  "paymentMethodDetails": {
    "card": {
      "number": "4242424242424242",
      "expiryMonth": 12,
      "expiryYear": 2025,
      "cvc": "123",
      "holderName": "John Doe"
    }
  }
}
```

## Security Considerations

### PCI Compliance

- **Never store raw card data** - Use provider tokenization
- **HTTPS only** for all payment endpoints
- **Secure configuration** of provider credentials
- **Audit logging** of all payment operations

### Authentication

- All endpoints require valid JWT tokens
- User context extracted from JWT claims
- Correlation ID tracking for request tracing

## Monitoring and Logging

### Structured Logging

- Correlation ID in all log messages
- Payment provider specific logging
- Error tracking with contextual information
- Performance metrics and timing

### Health Checks

- Database connectivity checks
- Payment provider health status
- Available at `/health` endpoint

## Testing

### Provider Testing

- **Stripe**: Use test card numbers (`4242424242424242`)
- **PayPal**: Use sandbox environment
- **Square**: Use sandbox environment

### Integration Testing

- Payment processing flows
- Refund scenarios
- Payment method management
- Error handling cases

## Deployment

### Production Considerations

1. **Secure Configuration**: Use Azure Key Vault or similar for secrets
2. **Database**: Use production SQL Server instance
3. **HTTPS**: Ensure all traffic is encrypted
4. **Monitoring**: Set up application insights and logging
5. **Backup**: Regular database backups
6. **Provider Configuration**: Use production provider credentials

### Environment Variables

```bash
ConnectionStrings__DefaultConnection="production-connection-string"
PaymentProviders__Stripe__SecretKey="sk_live_..."
PaymentProviders__PayPal__ClientSecret="production-secret"
Jwt__Key="production-jwt-key"
```

## Contributing

1. Follow the existing code patterns
2. Add unit tests for new features
3. Update documentation
4. Ensure PCI compliance considerations
5. Test with all supported payment providers

## License

This project is part of the xshopai microservices architecture.
