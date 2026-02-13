using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace PaymentService.Messaging;

/// <summary>
/// RabbitMQ-based messaging provider implementation.
/// Directly connects to RabbitMQ without Dapr abstraction.
/// </summary>
public class RabbitMQMessagingProvider : IMessagingProvider
{
    private readonly ILogger<RabbitMQMessagingProvider> _logger;
    private readonly string _connectionString;
    private readonly string _exchangeName;
    private bool _disposed;

    // RabbitMQ connection objects (lazy initialized)
    private IConnection? _connection;
    private IModel? _channel;
    private readonly object _lock = new();

    public string ProviderName => "rabbitmq";

    public RabbitMQMessagingProvider(
        ILogger<RabbitMQMessagingProvider> logger,
        string connectionString,
        string exchangeName = "xshopai.events")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _exchangeName = exchangeName;
    }

    /// <summary>
    /// Gets or creates the RabbitMQ channel (thread-safe).
    /// </summary>
    private IModel GetChannel()
    {
        if (_channel != null && _channel.IsOpen)
        {
            return _channel;
        }

        lock (_lock)
        {
            if (_channel != null && _channel.IsOpen)
            {
                return _channel;
            }

            // Close existing connection if needed
            if (_connection != null)
            {
                try
                {
                    _connection.Close();
                    _connection.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing existing RabbitMQ connection");
                }
            }

            // Create new connection
            var factory = new ConnectionFactory
            {
                Uri = new Uri(_connectionString),
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                RequestedHeartbeat = TimeSpan.FromSeconds(60)
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Declare exchange (idempotent - safe to call multiple times)
            _channel.ExchangeDeclare(
                exchange: _exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);

            _logger.LogInformation(
                "RabbitMQ connection established: Exchange={Exchange}",
                _exchangeName);

            return _channel;
        }
    }

    public async Task<bool> PublishEventAsync(
        string topic,
        object eventData,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        return await PublishEventInternalAsync(topic, eventData, correlationId, cancellationToken);
    }

    public async Task<bool> PublishEventAsync<T>(
        string topic,
        T eventData,
        string? correlationId = null,
        CancellationToken cancellationToken = default) where T : class
    {
        return await PublishEventInternalAsync(topic, eventData, correlationId, cancellationToken);
    }

    private Task<bool> PublishEventInternalAsync(
        string topic,
        object eventData,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Publishing event via RabbitMQ: Exchange={Exchange}, Topic={Topic}, CorrelationId={CorrelationId}",
                _exchangeName,
                topic,
                correlationId ?? "N/A");

            // Serialize the message
            var messageJson = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
            var messageBody = Encoding.UTF8.GetBytes(messageJson);

            // Get channel and publish
            var channel = GetChannel();

            // Set message properties
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true; // Survive broker restart
            properties.ContentType = "application/json";
            properties.CorrelationId = correlationId ?? Guid.NewGuid().ToString();
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            properties.AppId = "payment-service";

            // Publish to exchange with topic as routing key
            channel.BasicPublish(
                exchange: _exchangeName,
                routingKey: topic,
                basicProperties: properties,
                body: messageBody);

            _logger.LogInformation(
                "Successfully published event via RabbitMQ: Topic={Topic}, CorrelationId={CorrelationId}, Size={Size} bytes",
                topic,
                correlationId ?? "N/A",
                messageBody.Length);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish event via RabbitMQ: Topic={Topic}, CorrelationId={CorrelationId}",
                topic,
                correlationId ?? "N/A");
            return Task.FromResult(false);
        }
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if connection and channel are open
            var isHealthy = _connection != null && _connection.IsOpen &&
                          _channel != null && _channel.IsOpen;

            if (!isHealthy)
            {
                _logger.LogWarning("RabbitMQ health check failed: Connection or channel not open");
            }

            return Task.FromResult(isHealthy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during RabbitMQ health check");
            return Task.FromResult(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            // Close channel
            if (_channel != null && _channel.IsOpen)
            {
                _channel.Close();
                _channel.Dispose();
            }

            // Close connection
            if (_connection != null && _connection.IsOpen)
            {
                _connection.Close();
                _connection.Dispose();
            }
            
            _logger.LogInformation("RabbitMQ messaging provider disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing RabbitMQ messaging provider");
        }

        _disposed = true;
        await Task.CompletedTask;
    }
}
