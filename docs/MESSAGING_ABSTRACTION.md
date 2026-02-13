# Payment Service - Messaging Abstraction Layer

## Overview

Payment service now has a **complete messaging abstraction layer** that works seamlessly in both modes:

- ✅ **Without Dapr** (Direct RabbitMQ): `MESSAGING_PROVIDER=rabbitmq`
- ✅ **With Dapr** (Via Dapr sidecar): `MESSAGING_PROVIDER=dapr` (default)

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  PaymentService.Services / Controllers                          │
│              │                                                   │
│              ▼                                                   │
│  ┌───────────────────────────────────────────────────┐           │
│  │        IMessagingProvider (Interface)              │           │
│  └───────────────────────────────────────────────────┘           │
│              │                                                   │
│              ├──────────────────┬────────────────────┐           │
│              ▼                  ▼                    ▼           │
│  ┌──────────────────┐ ┌──────────────────┐ ┌─────────────────┐ │
│  │ DaprMessaging    │ │ RabbitMQMessaging│ │ ServiceBus      │ │
│  │   Provider       │ │   Provider       │ │  Messaging      │ │
│  └──────────────────┘ └──────────────────┘ │  Provider       │ │
│              │                  │           └─────────────────┘ │
│              ▼                  ▼                    ▼           │
│  ┌──────────────────┐ ┌──────────────────┐ ┌─────────────────┐ │
│  │  Dapr Sidecar    │ │  RabbitMQ.Client │ │ Azure.Messaging │ │
│  │  (Port 3500)     │ │  (Direct AMQP)   │ │  .ServiceBus    │ │
│  └──────────────────┘ └──────────────────┘ └─────────────────┘ │
│              │                  │                    │           │
└──────────────┼──────────────────┼────────────────────┼───────────┘
               │                  │                    │
               ▼                  ▼                    ▼
         ┌──────────────────────────────────────────────┐
         │        Message Broker Infrastructure         │
         │  (RabbitMQ / Kafka / Azure Service Bus)      │
         └──────────────────────────────────────────────┘
```

## Configuration

### Option 1: Direct RabbitMQ (Without Dapr)

**appsettings.json**:

```json
{
  "MESSAGING_PROVIDER": "rabbitmq",
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "admin",
    "Password": "admin123",
    "VirtualHost": "/",
    "ExchangeName": "xshopai.events"
  }
}
```

**Environment Variables**:

```bash
MESSAGING_PROVIDER=rabbitmq
RABBITMQ_HOST=localhost
RABBITMQ_PORT=5672
RABBITMQ_USERNAME=admin
RABBITMQ_PASSWORD=admin123
```

### Option 2: Dapr (Via Dapr Sidecar)

**appsettings.json**:

```json
{
  "MESSAGING_PROVIDER": "dapr",
  "Dapr": {
    "Enabled": true
  }
}
```

**Environment Variables**:

```bash
MESSAGING_PROVIDER=dapr
DAPR_HTTP_PORT=3500
DAPR_GRPC_PORT=50001
DAPR_PUBSUB_NAME=pubsub
```

### Option 3: Azure Service Bus

**appsettings.json**:

```json
{
  "MESSAGING_PROVIDER": "servicebus",
  "ServiceBus": {
    "ConnectionString": "Endpoint=sb://..."
  }
}
```

## Usage in Code

### Publishing Events

```csharp
using PaymentService.Messaging;

public class PaymentService
{
    private readonly IMessagingProvider _messagingProvider;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IMessagingProvider messagingProvider,
        ILogger<PaymentService> logger)
    {
        _messagingProvider = messagingProvider;
        _logger = logger;
    }

    public async Task ProcessPaymentAsync(PaymentRequest request)
    {
        // Process payment...
        var payment = await _paymentGateway.ChargeAsync(request);

        // Publish event (works with any provider)
        var eventData = new
        {
            PaymentId = payment.Id,
            OrderId = request.OrderId,
            Amount = payment.Amount,
            Status = payment.Status
        };

        var published = await _messagingProvider.PublishEventAsync(
            topic: "payment.received",
            eventData: eventData,
            correlationId: request.CorrelationId
        );

        if (published)
        {
            _logger.LogInformation("Payment event published successfully");
        }
    }
}
```

### Dependency Injection Registration

**Program.cs**:

```csharp
// Add messaging abstraction (auto-detects provider from config)
builder.Services.AddMessaging(builder.Configuration);

// IMessagingProvider is now available for injection
```

## Implementation Details

### MessagingProviderFactory

The factory creates the appropriate provider based on configuration:

```csharp
public IMessagingProvider CreateProvider()
{
    var providerName = GetConfiguredProviderName();
    var providerType = ParseProviderType(providerName);

    return providerType switch
    {
        MessagingProviderType.Dapr => CreateDaprProvider(),
        MessagingProviderType.RabbitMQ => CreateRabbitMQProvider(),
        MessagingProviderType.ServiceBus => CreateServiceBusProvider(),
        _ => throw new InvalidOperationException($"Unsupported provider")
    };
}
```

### RabbitMQMessagingProvider Features

✅ **Connection Management**:

- Thread-safe connection pooling
- Auto-reconnection on failure
- Heartbeat monitoring (60s interval)
- Network recovery (10s interval)

✅ **Exchange Declaration**:

- Topic exchange type
- Durable (survives broker restart)
- Idempotent (safe to call multiple times)

✅ **Message Publishing**:

- Persistent messages
- JSON serialization
- Correlation ID tracking
- Automatic encoding (UTF-8)

✅ **Health Checks**:

- Connection status monitoring
- Channel availability checking

✅ **Resource Cleanup**:

- Proper disposal of connections
- Channel cleanup on shutdown

### DaprMessagingProvider Features

✅ **Dapr Integration**:

- Uses DaprClient SDK
- Pub/sub abstraction
- CloudEvents format
- Automatic retries

✅ **Configuration**:

- Configurable pub/sub component name
- Health check via Dapr sidecar

## Benefits

### 1. **Flexibility**

Switch messaging infrastructure without code changes:

- Development: Direct RabbitMQ (no Dapr overhead)
- Production: Dapr (for observability and resilience)
- Azure: Azure Service Bus (cloud-native)

### 2. **Testability**

Easy to mock IMessagingProvider for unit tests:

```csharp
var mockMessaging = new Mock<IMessagingProvider>();
mockMessaging
    .Setup(m => m.PublishEventAsync(It.IsAny<string>(), It.IsAny<object>(), null, default))
    .ReturnsAsync(true);
```

### 3. **Consistency**

Same code works across all environments:

- No environment-specific conditionals
- Configuration-driven behavior
- Type-safe API

### 4. **Observability**

Built-in logging at all levels:

- Connection events
- Publishing attempts
- Failures with context
- Performance metrics

## Migration Guide

### From Hardcoded Dapr to Abstraction

**Before**:

```csharp
var daprClient = serviceProvider.GetRequiredService<DaprClient>();
await daprClient.PublishEventAsync("pubsub", "payment.received", eventData);
```

**After**:

```csharp
var messaging = serviceProvider.GetRequiredService<IMessagingProvider>();
await messaging.PublishEventAsync("payment.received", eventData);
```

### From DaprEventPublisher to IMessagingProvider

**Before**:

```csharp
private readonly DaprEventPublisher _eventPublisher;

await _eventPublisher.PublishPaymentReceivedAsync(payment);
```

**After**:

```csharp
private readonly IMessagingProvider _messagingProvider;

await _messagingProvider.PublishEventAsync("payment.received", payment);
```

## Troubleshooting

### RabbitMQ Connection Failed

**Error**: `Failed to publish event via RabbitMQ`

**Solution**:

1. Check RabbitMQ is running: `docker ps | grep rabbitmq`
2. Verify credentials in appsettings.json
3. Check connection string format: `amqp://user:pass@host:port/vhost`
4. Ensure exchange exists: `xshopai.events`

### Dapr Client Not Available

**Error**: `DaprClient is not available`

**Solution**:

1. Start Dapr sidecar: `dapr run --app-id payment-service ...`
2. Check Dapr HTTP port: `DAPR_HTTP_PORT=3500`
3. Verify pub/sub component: `.dapr/components/pubsub.yaml`

### Wrong Provider Used

**Error**: Unexpected behavior with messaging

**Solution**:

1. Check `MESSAGING_PROVIDER` environment variable
2. Verify appsettings.json configuration
3. Look for startup logs: `✅ Messaging Provider: rabbitmq`

## Best Practices

1. **Always use IMessagingProvider** - Don't reference concrete providers directly
2. **Set correlation IDs** - For distributed tracing
3. **Handle publish failures gracefully** - Don't block main flow on messaging errors
4. **Use environment variables** - For environment-specific configuration
5. **Test both modes** - Ensure compatibility with Dapr and direct messaging
6. **Monitor health checks** - Track connection status
7. **Log failures with context** - Include correlation ID and topic name

## Related Files

- `PaymentService/Messaging/IMessagingProvider.cs` - Interface definition
- `PaymentService/Messaging/DaprMessagingProvider.cs` - Dapr implementation
- `PaymentService/Messaging/RabbitMQMessagingProvider.cs` - RabbitMQ implementation
- `PaymentService/Messaging/MessagingProviderFactory.cs` - Factory + DI extensions
- `PaymentService/appsettings.json` - Configuration
- `PaymentService/Program.cs` - DI registration
