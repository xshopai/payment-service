using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace PaymentService.Messaging;

/// <summary>
/// Dapr-based messaging provider implementation.
/// Uses Dapr pub/sub for message broker abstraction.
/// </summary>
public class DaprMessagingProvider : IMessagingProvider
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<DaprMessagingProvider> _logger;
    private readonly string _pubSubName;
    private bool _disposed;

    public string ProviderName => "dapr";

    public DaprMessagingProvider(
        DaprClient daprClient,
        ILogger<DaprMessagingProvider> logger,
        string pubSubName = "pubsub")
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pubSubName = pubSubName;
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

    private async Task<bool> PublishEventInternalAsync(
        string topic,
        object eventData,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Publishing event via Dapr: PubSub={PubSubName}, Topic={Topic}, CorrelationId={CorrelationId}",
                _pubSubName,
                topic,
                correlationId ?? "N/A");

            await _daprClient.PublishEventAsync(
                _pubSubName,
                topic,
                eventData,
                cancellationToken);

            _logger.LogInformation(
                "Successfully published event via Dapr: Topic={Topic}, CorrelationId={CorrelationId}",
                topic,
                correlationId ?? "N/A");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish event via Dapr: Topic={Topic}, CorrelationId={CorrelationId}",
                topic,
                correlationId ?? "N/A");
            return false;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _daprClient.CheckHealthAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dapr health check failed");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        _disposed = true;
        _logger.LogInformation("Dapr messaging provider disposed");
        await Task.CompletedTask;
    }
}
