namespace PaymentService.Messaging;

/// <summary>
/// Interface for messaging provider abstraction.
/// Supports multiple message broker implementations (Dapr, RabbitMQ, Azure Service Bus).
/// </summary>
public interface IMessagingProvider : IAsyncDisposable
{
    /// <summary>
    /// Gets the name of the messaging provider.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Publishes an event to the specified topic.
    /// </summary>
    /// <param name="topic">The topic/routing key to publish to (e.g., "payment.created")</param>
    /// <param name="eventData">The event payload to publish</param>
    /// <param name="correlationId">Optional correlation ID for distributed tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the event was published successfully, false otherwise</returns>
    Task<bool> PublishEventAsync(
        string topic,
        object eventData,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an event to the specified topic with strongly-typed data.
    /// </summary>
    /// <typeparam name="T">The type of the event data</typeparam>
    /// <param name="topic">The topic/routing key to publish to</param>
    /// <param name="eventData">The event payload to publish</param>
    /// <param name="correlationId">Optional correlation ID for distributed tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the event was published successfully, false otherwise</returns>
    Task<bool> PublishEventAsync<T>(
        string topic,
        T eventData,
        string? correlationId = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Checks if the messaging provider is healthy and connected.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if healthy, false otherwise</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
