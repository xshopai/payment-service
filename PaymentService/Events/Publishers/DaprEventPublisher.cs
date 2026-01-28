using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace PaymentService.Events.Publishers;

/// <summary>
/// Dapr-based event publisher for message broker integration
/// Uses Dapr pub/sub to publish events to RabbitMQ
/// </summary>
public class DaprEventPublisher
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<DaprEventPublisher> _logger;
    private const string PubSubName = "pubsub";

    public DaprEventPublisher(
        DaprClient daprClient,
        ILogger<DaprEventPublisher> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    /// <summary>
    /// Publishes an event using Dapr pub/sub
    /// </summary>
    /// <typeparam name="T">Message payload type</typeparam>
    /// <param name="routingKey">Topic/routing key (e.g., "payment.created")</param>
    /// <param name="message">Message payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task PublishEventAsync<T>(
        string routingKey,
        T message,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            _logger.LogInformation(
                "Publishing event via Dapr: PubSub={PubSubName}, Topic={RoutingKey}",
                PubSubName,
                routingKey);

            await _daprClient.PublishEventAsync(
                PubSubName,
                routingKey,
                message,
                cancellationToken);

            _logger.LogInformation(
                "Successfully published event via Dapr: Topic={RoutingKey}",
                routingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish event via Dapr: Topic={RoutingKey}",
                routingKey);
            throw;
        }
    }
}
