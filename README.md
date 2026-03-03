<div align="center">

# 💳 Payment Service

**Multi-provider payment processing microservice for the xshopai e-commerce platform**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![C#](https://img.shields.io/badge/C%23-12-239120?style=for-the-badge&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![SQL Server](https://img.shields.io/badge/SQL_Server-2022-CC2927?style=for-the-badge&logo=microsoftsqlserver&logoColor=white)](https://www.microsoft.com/sql-server)
[![Dapr](https://img.shields.io/badge/Dapr-Enabled-0D597F?style=for-the-badge&logo=dapr&logoColor=white)](https://dapr.io)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)

[Getting Started](#-getting-started) •
[Documentation](#-documentation) •
[API Reference](#-api-reference) •
[Contributing](#-contributing)

</div>

---

## 🎯 Overview

The **Payment Service** handles payment transactions, refunds, and payment method management across multiple providers (Stripe, PayPal, Square). Built with a provider abstraction layer, it supports multi-currency operations, PCI compliance considerations, and idempotent payment processing with full audit trails.

---

## ✨ Key Features

<table>
<tr>
<td width="50%">

### 💳 Multi-Provider Payments

- Stripe, PayPal, and Square integration
- Provider abstraction layer
- Multi-currency support (USD, EUR, GBP, CAD)
- Idempotent payment processing

</td>
<td width="50%">

### 🔄 Refund Management

- Full and partial refunds
- Refund reason tracking
- Provider-specific refund handling
- Refund status monitoring

</td>
</tr>
<tr>
<td width="50%">

### 💾 Payment Methods

- Secure payment method storage
- Customer payment profiles
- Default method selection
- Provider tokenization

</td>
<td width="50%">

### 🛡️ Security & Compliance

- PCI compliance considerations
- JWT Bearer authentication
- Provider credential security
- Complete transaction audit trails

</td>
</tr>
</table>

---

## 🚀 Getting Started

### Prerequisites

- .NET 8 SDK
- SQL Server 2019+
- Docker & Docker Compose (optional)
- Dapr CLI (for production-like setup)

### Quick Start with Docker Compose

```bash
# Clone the repository
git clone https://github.com/xshopai/payment-service.git
cd payment-service

# Start SQL Server + service
docker-compose up -d

# Verify the service is healthy
curl http://localhost:8009/health
```

### Local Development Setup

<details>
<summary><b>🔧 Without Dapr (Simple Setup)</b></summary>

```bash
# Restore dependencies
dotnet restore

# Set up environment variables
cp .env.example .env
# Edit .env with your configuration

# Start SQL Server (Docker)
docker-compose -f docker-compose.db.yml up -d

# Apply migrations
dotnet ef database update --project PaymentService

# Run the service
dotnet run --project PaymentService
```

📖 See [Local Development Guide](docs/LOCAL_DEVELOPMENT.md) for detailed instructions.

</details>

<details>
<summary><b>⚡ With Dapr (Production-like)</b></summary>

```bash
# Ensure Dapr is initialized
dapr init

# Start with Dapr sidecar
./run.sh       # Linux/Mac
.\run.ps1      # Windows

# Or manually
dapr run \
  --app-id payment-service \
  --app-port 8009 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --resources-path .dapr/components \
  --config .dapr/config.yaml \
  -- dotnet run --project PaymentService
```

> **Note:** All services now use the standard Dapr ports (3500 for HTTP, 50001 for gRPC).

</details>

---

## 📚 Documentation

| Document                                          | Description                                        |
| :------------------------------------------------ | :------------------------------------------------- |
| 📘 [Local Development](docs/LOCAL_DEVELOPMENT.md) | Step-by-step local setup and development workflows |
| 📘 [Technical Reference](docs/TECHNICAL.md)       | Architecture, security, monitoring                 |
| ☁️ [Azure Container Apps](docs/ACA_DEPLOYMENT.md) | Deploy to serverless containers with built-in Dapr |

**API Documentation**: Swagger UI available at `/swagger` endpoint.

---

## 🔌 API Reference

### Payments

| Method | Endpoint                        | Description             |
| :----- | :------------------------------ | :---------------------- |
| `POST` | `/api/payments`                 | Process a payment       |
| `GET`  | `/api/payments`                 | Get payments (filtered) |
| `GET`  | `/api/payments/{id}`            | Get specific payment    |
| `POST` | `/api/payments/{id}/refund`     | Process refund          |
| `GET`  | `/api/payments/order/{orderId}` | Get payment by order    |

### Payment Methods

| Method   | Endpoint                                      | Description           |
| :------- | :-------------------------------------------- | :-------------------- |
| `POST`   | `/api/paymentmethods`                         | Save payment method   |
| `GET`    | `/api/paymentmethods/customer/{customerId}`   | Customer's methods    |
| `DELETE` | `/api/paymentmethods/{id}`                    | Delete payment method |
| `GET`    | `/api/paymentmethods/supported-methods`       | Supported methods     |
| `GET`    | `/api/paymentmethods/providers`               | Available providers   |
| `GET`    | `/api/paymentmethods/providers/{name}/status` | Provider status       |

---

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Build without tests
dotnet build

# Run with specific configuration
dotnet test --configuration Release

# Apply migration
dotnet ef database update --project PaymentService
```

### Provider Testing

| Provider | Test Mode                     |
| :------- | :---------------------------- |
| Stripe   | Test card: `4242424242424242` |
| PayPal   | Sandbox environment           |
| Square   | Sandbox environment           |

---

## 🏗️ Project Structure

```
payment-service/
├── 📁 PaymentService/               # Main application project
│   ├── 📁 Controllers/              # REST API endpoints
│   ├── 📁 Services/                 # Business logic + provider adapters
│   ├── 📁 Models/
│   │   ├── 📁 Entities/             # Domain entities (Payment, Refund)
│   │   └── 📁 DTOs/                 # Data transfer objects
│   ├── 📁 Data/                     # EF Core context + migrations
│   ├── 📁 Configuration/            # Settings classes
│   └── 📄 Program.cs                # Application entry point
├── 📁 PaymentService.Tests/         # Unit tests
├── 📁 docs/                         # Documentation
├── 📁 .dapr/                        # Dapr configuration
│   ├── 📁 components/               # Pub/sub, state stores
│   └── 📄 config.yaml               # Dapr runtime configuration
├── 📄 docker-compose.yml            # Full service stack
├── 📄 docker-compose.db.yml         # SQL Server only
├── 📄 Dockerfile                    # Production container image
└── 📄 PaymentService.sln            # Solution file
```

---

## 🔧 Technology Stack

| Category          | Technology                                 |
| :---------------- | :----------------------------------------- |
| 🟣 Runtime        | .NET 8 / C# 12                             |
| 🌐 Framework      | ASP.NET Core 8                             |
| 🗄️ Database       | SQL Server 2022 with Entity Framework Core |
| 💳 Providers      | Stripe, PayPal, Square (pluggable)         |
| 📨 Messaging      | Dapr Pub/Sub (RabbitMQ backend)            |
| 🔐 Authentication | JWT Bearer Tokens                          |
| 📖 API Docs       | Swagger / OpenAPI (Swashbuckle)            |
| 🧪 Testing        | xUnit                                      |
| 📊 Observability  | Structured logging + correlation IDs       |

---

## ⚡ Quick Reference

```bash
# 🐳 Docker Compose
docker-compose up -d              # Start all services
docker-compose down               # Stop all services
docker-compose -f docker-compose.db.yml up -d  # SQL Server only

# 🟣 Local Development
dotnet run --project PaymentService  # Run service
dotnet watch --project PaymentService  # Hot reload

# ⚡ Dapr Development
./run.sh                          # Linux/Mac
.\run.ps1                         # Windows

# 🧪 Testing
dotnet test                       # Run all tests
dotnet build                      # Build solution

# 🔍 Health Check
curl http://localhost:8009/health
curl http://localhost:8009/swagger
```

---

## 🤝 Contributing

We welcome contributions! Please follow these steps:

1. **Fork** the repository
2. **Create** a feature branch
   ```bash
   git checkout -b feature/amazing-feature
   ```
3. **Write** tests for your changes
4. **Run** the test suite
   ```bash
   dotnet test
   ```
5. **Commit** your changes
   ```bash
   git commit -m 'feat: add amazing feature'
   ```
6. **Push** to your branch
   ```bash
   git push origin feature/amazing-feature
   ```
7. **Open** a Pull Request

Please ensure your PR:

- ✅ Passes all existing tests
- ✅ Includes tests for new functionality
- ✅ Follows PCI compliance considerations
- ✅ Updates documentation as needed

---

## 🆘 Support

| Resource         | Link                                                                         |
| :--------------- | :--------------------------------------------------------------------------- |
| 🐛 Bug Reports   | [GitHub Issues](https://github.com/xshopai/payment-service/issues)           |
| 📖 Documentation | [docs/](docs/)                                                               |
| 💬 Discussions   | [GitHub Discussions](https://github.com/xshopai/payment-service/discussions) |

---

## 📄 License

This project is part of the **xshopai** e-commerce platform.
Licensed under the MIT License - see [LICENSE](LICENSE) for details.

---

<div align="center">

**[⬆ Back to Top](#-payment-service)**

Made with ❤️ by the xshopai team

</div>
