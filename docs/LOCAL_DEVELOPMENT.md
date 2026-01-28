# Payment Service - Local Development Guide

## Prerequisites

- .NET 8 SDK
- SQL Server (local, Docker, or Azure SQL)
- Stripe account (for payment processing)
- Dapr CLI (for pub/sub)

## Quick Start

### 1. Start SQL Server

Using Docker:

```bash
docker run -d \
  --name sqlserver-payment \
  -e ACCEPT_EULA=Y \
  -e SA_PASSWORD=YourStrong@Passw0rd \
  -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest
```

### 2. Configure Application

Update `PaymentService/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=payment_db;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True"
  },
  "Stripe": {
    "ApiKey": "sk_test_your_stripe_key",
    "WebhookSecret": "whsec_your_webhook_secret"
  },
  "Dapr": {
    "HttpPort": 3500,
    "PubSubName": "xshopai-pubsub"
  }
}
```

### 3. Run Database Migrations

```bash
cd PaymentService
dotnet ef database update
```

### 4. Run the Service

Without Dapr:

```bash
dotnet run --project PaymentService
```

With Dapr:

```bash
./run.sh
# or on Windows
./run.ps1
```

## API Endpoints

| Method | Endpoint                    | Description            |
| ------ | --------------------------- | ---------------------- |
| GET    | `/health`                   | Health check           |
| POST   | `/api/payments`             | Process payment        |
| GET    | `/api/payments/{id}`        | Get payment details    |
| POST   | `/api/payments/{id}/refund` | Refund payment         |
| POST   | `/api/payments/webhook`     | Stripe webhook handler |

## Stripe Integration

### Test Mode

Use Stripe test API keys for development:

- Test card: `4242 4242 4242 4242`
- Any future expiry date
- Any 3-digit CVC

### Webhook Testing

Use Stripe CLI for local webhook testing:

```bash
stripe listen --forward-to localhost:1009/api/payments/webhook
```

## Published Events

| Event               | Trigger            |
| ------------------- | ------------------ |
| `payment.initiated` | Payment started    |
| `payment.completed` | Payment successful |
| `payment.failed`    | Payment failed     |
| `payment.refunded`  | Refund processed   |

## Security Considerations

- Never log full card numbers
- Use Stripe's tokenization
- Store only payment references, not card data
- Validate webhook signatures

## Troubleshooting

### Stripe Webhook Signature Invalid

- Ensure webhook secret is correct
- Check raw request body is preserved

### SQL Connection Issues

- Verify SQL Server is running
- Check firewall allows port 1433
