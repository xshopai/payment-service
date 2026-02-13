using Dapr.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PaymentService.Messaging;

/// <summary>
/// Supported messaging provider types.
/// </summary>
public enum MessagingProviderType
{
    Dapr,
    RabbitMQ,
    ServiceBus
}

/// <summary>
/// Factory for creating messaging provider instances based on configuration.
/// </summary>
public class MessagingProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MessagingProviderFactory> _logger;

    private const string DefaultProvider = "dapr";
    private const string ConfigKey = "MESSAGING_PROVIDER";
    private const string RabbitMQConnectionKey = "RABBITMQ_CONNECTION_STRING";
    private const string ServiceBusConnectionKey = "SERVICEBUS_CONNECTION_STRING";
    private const string DaprPubSubNameKey = "DAPR_PUBSUB_NAME";

    public MessagingProviderFactory(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<MessagingProviderFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a messaging provider based on the configured provider type.
    /// </summary>
    /// <returns>An instance of IMessagingProvider</returns>
    public IMessagingProvider CreateProvider()
    {
        var providerName = GetConfiguredProviderName();
        var providerType = ParseProviderType(providerName);
        
        _logger.LogInformation("Creating messaging provider: {ProviderType}", providerType);

        return providerType switch
        {
            MessagingProviderType.Dapr => CreateDaprProvider(),
            MessagingProviderType.RabbitMQ => CreateRabbitMQProvider(),
            MessagingProviderType.ServiceBus => CreateServiceBusProvider(),
            _ => throw new InvalidOperationException($"Unsupported messaging provider: {providerName}")
        };
    }

    /// <summary>
    /// Gets the configured provider name from configuration or environment.
    /// </summary>
    public string GetConfiguredProviderName()
    {
        // Try configuration first, then environment variable
        var providerName = _configuration[ConfigKey] 
            ?? _configuration[$"Messaging:{ConfigKey}"]
            ?? Environment.GetEnvironmentVariable(ConfigKey)
            ?? DefaultProvider;

        return providerName.ToLowerInvariant();
    }

    /// <summary>
    /// Parses the provider name string to MessagingProviderType enum.
    /// </summary>
    public static MessagingProviderType ParseProviderType(string providerName)
    {
        return providerName?.ToLowerInvariant() switch
        {
            "dapr" => MessagingProviderType.Dapr,
            "rabbitmq" => MessagingProviderType.RabbitMQ,
            "servicebus" => MessagingProviderType.ServiceBus,
            "azureservicebus" => MessagingProviderType.ServiceBus,
            _ => MessagingProviderType.Dapr // Default to Dapr
        };
    }

    private IMessagingProvider CreateDaprProvider()
    {
        var daprClient = _serviceProvider.GetService<DaprClient>();
        if (daprClient == null)
        {
            var errorMessage = "DaprClient is not available. When MESSAGING_PROVIDER=dapr, ensure Dapr sidecar is running. " +
                             "For direct messaging without Dapr, set MESSAGING_PROVIDER=rabbitmq or MESSAGING_PROVIDER=servicebus";
            _logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }
        
        var logger = _serviceProvider.GetRequiredService<ILogger<DaprMessagingProvider>>();
        
        var pubSubName = _configuration[DaprPubSubNameKey]
            ?? _configuration["Dapr:PubSubName"]
            ?? Environment.GetEnvironmentVariable(DaprPubSubNameKey)
            ?? "pubsub";

        _logger.LogInformation("Creating Dapr messaging provider with PubSub: {PubSubName}", pubSubName);

        return new DaprMessagingProvider(daprClient, logger, pubSubName);
    }

    private IMessagingProvider CreateRabbitMQProvider()
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<RabbitMQMessagingProvider>>();
        
        // Try to get direct connection string first
        var connectionString = _configuration[RabbitMQConnectionKey]
            ?? _configuration["RabbitMQ:ConnectionString"]
            ?? _configuration.GetConnectionString("RabbitMQ")
            ?? Environment.GetEnvironmentVariable(RabbitMQConnectionKey);

        // If no direct connection string, build from components
        if (string.IsNullOrEmpty(connectionString))
        {
            var host = _configuration["RabbitMQ:Host"] ?? "localhost";
            var port = _configuration["RabbitMQ:Port"] ?? "5672";
            var username = _configuration["RabbitMQ:Username"] ?? "admin";
            var password = _configuration["RabbitMQ:Password"] ?? "admin123";
            var virtualHost = _configuration["RabbitMQ:VirtualHost"] ?? "/";
            
            connectionString = $"amqp://{username}:{password}@{host}:{port}{virtualHost}";
            
            _logger.LogInformation(
                "Built RabbitMQ connection string from components: {Host}:{Port}",
                host, port);
        }

        var exchangeName = _configuration["RabbitMQ:ExchangeName"] 
            ?? Environment.GetEnvironmentVariable("RABBITMQ_EXCHANGE_NAME")
            ?? "xshopai.events";

        _logger.LogInformation("Creating RabbitMQ messaging provider with Exchange: {Exchange}", exchangeName);

        return new RabbitMQMessagingProvider(logger, connectionString, exchangeName);
    }

    private IMessagingProvider CreateServiceBusProvider()
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<ServiceBusMessagingProvider>>();
        
        var connectionString = _configuration[ServiceBusConnectionKey]
            ?? _configuration["ServiceBus:ConnectionString"]
            ?? _configuration.GetConnectionString("ServiceBus")
            ?? Environment.GetEnvironmentVariable(ServiceBusConnectionKey)
            ?? throw new InvalidOperationException("Azure Service Bus connection string not configured. Set SERVICEBUS_CONNECTION_STRING.");

        _logger.LogInformation("Creating Azure Service Bus messaging provider");

        return new ServiceBusMessagingProvider(logger, connectionString);
    }
}

/// <summary>
/// Extension methods for registering messaging services in DI container.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Adds messaging services to the DI container.
    /// Registers the factory and creates the appropriate provider based on configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register the factory
        services.AddSingleton<MessagingProviderFactory>();

        // Register IMessagingProvider using the factory
        services.AddSingleton<IMessagingProvider>(sp =>
        {
            var factory = sp.GetRequiredService<MessagingProviderFactory>();
            return factory.CreateProvider();
        });

        return services;
    }
}
