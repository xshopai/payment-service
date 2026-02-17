using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using PaymentService.Events.Models;
using PaymentService.Services;
using PaymentService.Utils;

namespace PaymentService.Messaging;

/// <summary>
/// Background service that consumes events from RabbitMQ when running without Dapr.
/// Only active when MESSAGING_PROVIDER=rabbitmq
/// </summary>
public class RabbitMQBackgroundConsumer : BackgroundService
{
    private readonly ILogger<RabbitMQBackgroundConsumer> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _connectionString;
    private readonly string _exchangeName;
    private readonly string _serviceName = "payment-service";
    private IConnection? _connection;
    private IModel? _channel;
    private readonly object _lock = new();

    public RabbitMQBackgroundConsumer(
        ILogger<RabbitMQBackgroundConsumer> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        // Read RabbitMQ configuration
        var host = configuration["RabbitMQ:Host"] ?? "localhost";
        var port = configuration["RabbitMQ:Port"] ?? "5672";
        var username = configuration["RabbitMQ:Username"] ?? "admin";
        var password = configuration["RabbitMQ:Password"] ?? "admin123";
        var virtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/";

        _connectionString = $"amqp://{username}:{password}@{host}:{port}{virtualHost}";
        _exchangeName = configuration["RabbitMQ:ExchangeName"] ?? "xshopai.events";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RabbitMQ Background Consumer starting...");

        try
        {
            // Create connection
            var factory = new ConnectionFactory
            {
                Uri = new Uri(_connectionString),
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                RequestedHeartbeat = TimeSpan.FromSeconds(60),
                DispatchConsumersAsync = true // Enable async consumers
            };

            _logger.LogInformation("Attempting to connect to RabbitMQ at {ConnectionString}", _connectionString.Replace(_connectionString.Split('@')[0].Split("//")[1], "***:***"));
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Declare exchange (idempotent)
            _channel.ExchangeDeclare(
                exchange: _exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);

            // Declare service-specific queue
            var queueName = $"{_serviceName}-queue";
            _channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false);

            // Bind queue to topics we're interested in
            var topics = new[]
            {
                "order.created",      // Primary topic for new orders
                "order.cancelled",   // For refund processing
                "payment.refund"     // Saga compensation - refund payment when order fails
            };

            foreach (var topic in topics)
            {
                _channel.QueueBind(queueName, _exchangeName, topic);
                _logger.LogInformation(
                    "Bound queue to topic: Queue={Queue}, Topic={Topic}",
                    queueName, topic);
            }

            // Set prefetch count (process one message at a time)
            _channel.BasicQos(0, 1, false);

            // Create async consumer
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (sender, ea) =>
            {
                await HandleMessageAsync(ea, stoppingToken);
            };

            // Start consuming
            _channel.BasicConsume(
                queue: queueName,
                autoAck: false, // Manual acknowledgment
                consumer: consumer);

            _logger.LogInformation(
                "✅ RabbitMQ consumer started: Queue={Queue}, Exchange={Exchange}",
                queueName, _exchangeName);

            // Keep running until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RabbitMQ background consumer - service will continue without event consumption");
            _logger.LogWarning("Payment service will run in HTTP-only mode (Dapr subscriptions). Direct RabbitMQ consumption disabled.");
            // Don't throw - allow service to start without RabbitMQ
            return;
        }
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        var messageBody = Encoding.UTF8.GetString(ea.Body.ToArray());
        var routingKey = ea.RoutingKey; // This is the topic name
        var correlationId = ea.BasicProperties?.CorrelationId ?? Guid.NewGuid().ToString();

        try
        {
            _logger.LogInformation(
                "📨 Received message: Topic={Topic}, CorrelationId={CorrelationId}",
                routingKey, correlationId);

            // Route to appropriate handler based on topic
            var handled = routingKey switch
            {
                "order.created" => await HandleOrderCreatedAsync(messageBody, correlationId, cancellationToken),
                "order.cancelled" => await HandleOrderCancelledAsync(messageBody, correlationId, cancellationToken),
                "payment.refund" => await HandlePaymentRefundAsync(messageBody, correlationId, cancellationToken),
                _ => false
            };

            if (handled)
            {
                // Acknowledge message
                _channel?.BasicAck(ea.DeliveryTag, false);
                _logger.LogInformation(
                    "✅ Message processed and acknowledged: Topic={Topic}, CorrelationId={CorrelationId}",
                    routingKey, correlationId);
            }
            else
            {
                // Reject and requeue
                _channel?.BasicNack(ea.DeliveryTag, false, true);
                _logger.LogWarning(
                    "⚠️ Message rejected (will be requeued): Topic={Topic}",
                    routingKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Error processing message: Topic={Topic}, CorrelationId={CorrelationId}",
                routingKey, correlationId);

            // Reject and requeue for retry
            _channel?.BasicNack(ea.DeliveryTag, false, true);
        }
    }

    private async Task<bool> HandleOrderCreatedAsync(string messageBody, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            // Deserialize event
            var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(messageBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (orderEvent == null)
            {
                _logger.LogWarning("Failed to deserialize order.created event");
                return false;
            }

            _logger.LogInformation(
                "🔄 Processing order.created event: OrderId={OrderId}, Amount={Amount}",
                orderEvent.OrderId, orderEvent.TotalAmount);

            // Create a scope to resolve scoped services
            using var scope = _serviceProvider.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var standardLogger = scope.ServiceProvider.GetRequiredService<IStandardLogger>();

            // Process payment
            var paymentRequest = new Models.DTOs.ProcessPaymentDto
            {
                OrderId = orderEvent.OrderId,
                CustomerId = orderEvent.CustomerId,
                Amount = orderEvent.TotalAmount,
                Currency = orderEvent.Currency ?? "USD",
                PaymentMethod = string.IsNullOrWhiteSpace(orderEvent.PaymentMethod) ? "credit_card" : orderEvent.PaymentMethod
            };

            var result = await paymentService.ProcessPaymentAsync(paymentRequest);

            if (result != null && result.IsSuccess)
            {
                standardLogger.Info(
                    $"✅ Payment processed successfully: PaymentId={result.PaymentId}",
                    correlationId,
                    new { paymentId = result.PaymentId, orderId = orderEvent.OrderId });
                
                // NOTE: In Admin-Driven workflow, we do NOT publish events here.
                // Admin must view payment in Admin UI and click "Confirm Payment"
                // to publish payment.processed event and advance the order saga.

                return true;
            }
            else
            {
                standardLogger.Error(
                    "❌ Payment processing failed - acknowledging message to prevent infinite retry",
                    null,
                    correlationId,
                    new { orderId = orderEvent.OrderId, error = result?.ErrorMessage });
                
                // IMPORTANT: Return true to acknowledge the message.
                // Payment failures are business logic failures (e.g., provider config error)
                // that won't succeed with retry. The failed payment record is saved in DB
                // and can be retried manually via Admin UI.
                // Returning false would cause infinite requeue loop.
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling order.created event");
            return false;
        }
    }

    private async Task<bool> HandleOrderCancelledAsync(string messageBody, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🔄 Processing order.cancelled event");

            // TODO: Implement refund logic when order is cancelled
            // For now, just log and acknowledge
            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling order.cancelled event");
            return false;
        }
    }

    private async Task<bool> HandlePaymentRefundAsync(string messageBody, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "🔄 SAGA COMPENSATION: Processing payment.refund event, CorrelationId={CorrelationId}",
                correlationId);

            // Deserialize refund event
            var refundEvent = JsonSerializer.Deserialize<Events.Models.PaymentRefundEvent>(messageBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (refundEvent == null || string.IsNullOrEmpty(refundEvent.PaymentId))
            {
                _logger.LogWarning("Failed to deserialize payment.refund event or missing PaymentId");
                return false;
            }

            using var scope = _serviceProvider.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var standardLogger = scope.ServiceProvider.GetRequiredService<IStandardLogger>();

            // Get the payment to refund
            var payment = await paymentService.GetPaymentAsync(Guid.Parse(refundEvent.PaymentId));

            if (payment == null)
            {
                standardLogger.Warn(
                    $"Payment not found for refund: {refundEvent.PaymentId} (may already be refunded)",
                    correlationId,
                    new { paymentId = refundEvent.PaymentId, orderId = refundEvent.OrderId });
                return true; // Idempotent - treat as success
            }

            var status = payment.Status.ToString().ToLower();
            if (status == "refunded" || status == "cancelled")
            {
                standardLogger.Info(
                    $"✅ Payment already refunded/cancelled: {refundEvent.PaymentId}",
                    correlationId,
                    new { paymentId = refundEvent.PaymentId, status = payment.Status });
                return true; // Idempotent - already compensated
            }

            // Process refund using the payment's amount
            var refundRequest = new Models.DTOs.ProcessRefundDto
            {
                PaymentId = refundEvent.PaymentId,
                Amount = payment.Amount,
                Reason = refundEvent.Reason ?? "Saga compensation - order failed"
            };

            var refundResult = await paymentService.ProcessRefundAsync(refundRequest);

            if (refundResult != null && refundResult.IsSuccess)
            {
                standardLogger.Info(
                    $"✅ SAGA COMPENSATION: Payment refunded successfully: {refundEvent.PaymentId}",
                    correlationId,
                    new { paymentId = refundEvent.PaymentId, orderId = refundEvent.OrderId, refundId = refundResult.RefundId });
                return true;
            }
            else
            {
                standardLogger.Error(
                    $"❌ SAGA COMPENSATION: Failed to refund payment: {refundEvent.PaymentId}",
                    null,
                    correlationId,
                    new { paymentId = refundEvent.PaymentId, orderId = refundEvent.OrderId, error = refundResult?.ErrorMessage });
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling payment.refund event");
            return false;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RabbitMQ Background Consumer stopping...");

        try
        {
            _channel?.Close();
            _connection?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping RabbitMQ consumer");
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
