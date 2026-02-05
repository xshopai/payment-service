using Dapr.Client;

namespace PaymentService.Services;

/// <summary>
/// Service for retrieving secrets from Dapr Secret Store with environment variable fallback
/// Supports payment provider credentials, JWT keys, and database connection strings
/// </summary>
public class DaprSecretService
{
    private readonly DaprClient? _daprClient;
    private readonly ILogger<DaprSecretService> _logger;
    private readonly IConfiguration _configuration;
    private readonly bool _daprEnabled;
    private const string SecretStoreName = "secretstore";

    public DaprSecretService(DaprClient? daprClient, ILogger<DaprSecretService> logger, IConfiguration configuration)
    {
        _daprClient = daprClient;
        _logger = logger;
        _configuration = configuration;
        
        // Check if MESSAGING_PROVIDER is set to something other than dapr
        var messagingProvider = configuration["MESSAGING_PROVIDER"] 
            ?? Environment.GetEnvironmentVariable("MESSAGING_PROVIDER")
            ?? "dapr";
        _daprEnabled = messagingProvider.Equals("dapr", StringComparison.OrdinalIgnoreCase) && daprClient != null;
        
        if (_daprEnabled)
        {
            _logger.LogInformation("Dapr Secret Service initialized (Dapr enabled)");
        }
        else
        {
            _logger.LogInformation("Dapr Secret Service initialized (Dapr disabled - using env vars only)");
        }
    }

    /// <summary>
    /// Get a secret value with environment variable priority.
    /// Priority:
    /// 1. Configuration/environment variables (Azure deployment - injected from Key Vault)
    /// 2. Dapr secret store (local development with .dapr/secrets.json)
    /// </summary>
    /// <param name="secretName">Name of the secret (e.g., "Jwt:Secret" or "JWT_SECRET")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Secret value or null if not found</returns>
    public async Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        // First try configuration/environment variables (Azure deployment)
        // Try multiple formats:
        // 1. Original format (e.g., "JWT_SECRET")
        // 2. Colon to double underscore (e.g., "Jwt:Secret" -> "Jwt__Secret")
        // 3. Colon to single underscore, uppercase (e.g., "Jwt:Secret" -> "JWT_SECRET")
        var configValue = _configuration[secretName];
        
        if (string.IsNullOrEmpty(configValue) && secretName.Contains(":"))
        {
            var doubleUnderscoreKey = secretName.Replace(":", "__");
            configValue = _configuration[doubleUnderscoreKey];
            
            if (string.IsNullOrEmpty(configValue))
            {
                var upperUnderscoreKey = secretName.Replace(":", "_").ToUpperInvariant();
                configValue = _configuration[upperUnderscoreKey] ?? Environment.GetEnvironmentVariable(upperUnderscoreKey);
            }
        }
        
        if (!string.IsNullOrEmpty(configValue))
        {
            _logger.LogDebug("Retrieved secret from configuration: {SecretName}", secretName);
            return configValue;
        }

        // Fallback to Dapr secret store (local development)
        if (_daprEnabled && _daprClient != null)
        {
            try
            {
                _logger.LogDebug("Retrieving secret from Dapr: {SecretName} from store: {StoreName}", 
                    secretName, SecretStoreName);

                var secrets = await _daprClient.GetSecretAsync(
                    SecretStoreName,
                    secretName,
                    cancellationToken: cancellationToken);

                if (secrets != null && secrets.Count > 0)
                {
                    var value = secrets.FirstOrDefault().Value;
                    _logger.LogDebug("Successfully retrieved secret from Dapr: {SecretName}", secretName);
                    return value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Dapr secret store unavailable for: {SecretName}", secretName);
            }
        }

        _logger.LogWarning("Secret not found in configuration or Dapr: {SecretName}", secretName);
        return null;
    }

    /// <summary>
    /// Get JWT configuration from secrets or environment variables
    /// </summary>
    public async Task<(string? Key, string? Issuer, string? Audience)> GetJwtConfigAsync(CancellationToken cancellationToken = default)
    {
        var key = await GetSecretAsync("Jwt:Secret", cancellationToken);
        var issuer = await GetSecretAsync("Jwt:Issuer", cancellationToken);
        var audience = await GetSecretAsync("Jwt:Audience", cancellationToken);

        return (key, issuer, audience);
    }

    /// <summary>
    /// Get database connection string from secrets
    /// </summary>
    public async Task<string?> GetDatabaseConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        // Try DATABASE_CONNECTION_STRING first (deployment standard)
        var connectionString = await GetSecretAsync("DATABASE_CONNECTION_STRING", cancellationToken);
        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString;
        }
        
        // Fallback to ConnectionStrings:DefaultConnection (legacy format)
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
