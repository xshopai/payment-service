using Dapr.Client;

namespace PaymentService.Services;

/// <summary>
/// Service for retrieving secrets from Dapr Secret Store
/// Supports payment provider credentials, JWT keys, and database connection strings
/// </summary>
public class DaprSecretService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<DaprSecretService> _logger;
    private const string SecretStoreName = "secretstore";

    public DaprSecretService(DaprClient daprClient, ILogger<DaprSecretService> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    /// <summary>
    /// Get a secret value from Dapr Secret Store
    /// </summary>
    /// <param name="secretName">Name of the secret (e.g., "jwt:secret")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Secret value or null if not found</returns>
    public async Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving secret: {SecretName} from store: {StoreName}", 
                secretName, SecretStoreName);

            var secrets = await _daprClient.GetSecretAsync(
                SecretStoreName,
                secretName,
                cancellationToken: cancellationToken);

            if (secrets == null || secrets.Count == 0)
            {
                _logger.LogWarning("Secret not found: {SecretName} in store: {StoreName}", secretName, SecretStoreName);
                return null;
            }

            var value = secrets.FirstOrDefault().Value;
            _logger.LogDebug("Successfully retrieved secret: {SecretName}", secretName);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret: {SecretName}", secretName);
            return null;
        }
    }

    /// <summary>
    /// Get JWT configuration from secrets
    /// </summary>
    public async Task<(string? Key, string? Issuer, string? Audience)> GetJwtConfigAsync(CancellationToken cancellationToken = default)
    {
        var key = await GetSecretAsync("Jwt:Key", cancellationToken);
        var issuer = await GetSecretAsync("Jwt:Issuer", cancellationToken);
        var audience = await GetSecretAsync("Jwt:Audience", cancellationToken);

        return (key, issuer, audience);
    }

    /// <summary>
    /// Get database connection string from secrets
    /// </summary>
    public async Task<string?> GetDatabaseConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        return await GetSecretAsync("ConnectionStrings:DefaultConnection", cancellationToken);
    }

    /// <summary>
    /// Get Stripe API keys from secrets
    /// </summary>
    public async Task<(string? PublishableKey, string? SecretKey, string? WebhookSecret)> GetStripeKeysAsync(CancellationToken cancellationToken = default)
    {
        var publishableKey = await GetSecretAsync("Stripe:PublishableKey", cancellationToken);
        var secretKey = await GetSecretAsync("Stripe:SecretKey", cancellationToken);
        var webhookSecret = await GetSecretAsync("Stripe:WebhookSecret", cancellationToken);

        return (publishableKey, secretKey, webhookSecret);
    }

    /// <summary>
    /// Get PayPal credentials from secrets
    /// </summary>
    public async Task<(string? ClientId, string? ClientSecret)> GetPayPalCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var clientId = await GetSecretAsync("PayPal:ClientId", cancellationToken);
        var clientSecret = await GetSecretAsync("PayPal:ClientSecret", cancellationToken);

        return (clientId, clientSecret);
    }

    /// <summary>
    /// Get Square credentials from secrets
    /// </summary>
    public async Task<(string? ApplicationId, string? AccessToken, string? LocationId)> GetSquareCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var applicationId = await GetSecretAsync("Square:ApplicationId", cancellationToken);
        var accessToken = await GetSecretAsync("Square:AccessToken", cancellationToken);
        var locationId = await GetSecretAsync("Square:LocationId", cancellationToken);

        return (applicationId, accessToken, locationId);
    }

    /// <summary>
    /// Get Redis connection string from secrets
    /// </summary>
    public async Task<string?> GetRedisConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        return await GetSecretAsync("Redis:ConnectionString", cancellationToken);
    }

    /// <summary>
    /// Check if Dapr secret store is healthy
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to retrieve a test secret to check connectivity
            await _daprClient.GetSecretAsync(SecretStoreName, "health-check", cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dapr secret store health check failed");
            return false;
        }
    }
}
