namespace PaymentService.Services;

/// <summary>
/// Service for retrieving secrets from environment variables/configuration
/// Supports payment provider credentials, JWT keys, and database connection strings
/// </summary>
public class DaprSecretService
{
    private readonly ILogger<DaprSecretService> _logger;
    private readonly IConfiguration _configuration;

    public DaprSecretService(ILogger<DaprSecretService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _logger.LogInformation("Secret Service initialized (using environment variables)");
    }

    /// <summary>
    /// Get a secret value from environment variables/configuration
    /// </summary>
    /// <param name="secretName">Name of the secret (e.g., "Jwt:Secret" or "JWT_SECRET")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Secret value or null if not found</returns>
    public Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        // Try configuration/environment variables
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
            return Task.FromResult<string?>(configValue);
        }

        _logger.LogWarning("Secret not found in configuration: {SecretName}", secretName);
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Get JWT configuration from environment variables
    /// </summary>
    public Task<(string? Key, string? Issuer, string? Audience)> GetJwtConfigAsync(CancellationToken cancellationToken = default)
    {
        var key = _configuration["JWT_SECRET"] ?? _configuration["Jwt:Secret"];
        var issuer = _configuration["Jwt:Issuer"] ?? "auth-service";
        var audience = _configuration["Jwt:Audience"] ?? "xshopai-platform";

        return Task.FromResult((key, issuer, audience));
    }

    /// <summary>
    /// Get database connection string from environment variables
    /// </summary>
    public Task<string?> GetDatabaseConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        // Try DATABASE_CONNECTION_STRING first (deployment standard)
        var connectionString = _configuration["DATABASE_CONNECTION_STRING"];
        if (!string.IsNullOrEmpty(connectionString))
        {
            return Task.FromResult<string?>(connectionString);
        }
        
        // Fallback to ConnectionStrings:DefaultConnection (legacy format)
        return Task.FromResult<string?>(_configuration["ConnectionStrings:DefaultConnection"]);
    }

    /// <summary>
    /// Get Stripe API keys from environment variables
    /// </summary>
    public Task<(string? PublishableKey, string? SecretKey, string? WebhookSecret)> GetStripeKeysAsync(CancellationToken cancellationToken = default)
    {
        var publishableKey = _configuration["Stripe:PublishableKey"] ?? _configuration["STRIPE_PUBLISHABLE_KEY"];
        var secretKey = _configuration["Stripe:SecretKey"] ?? _configuration["STRIPE_SECRET_KEY"];
        var webhookSecret = _configuration["Stripe:WebhookSecret"] ?? _configuration["STRIPE_WEBHOOK_SECRET"];

        return Task.FromResult((publishableKey, secretKey, webhookSecret));
    }

    /// <summary>
    /// Get PayPal credentials from environment variables
    /// </summary>
    public Task<(string? ClientId, string? ClientSecret)> GetPayPalCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var clientId = _configuration["PayPal:ClientId"] ?? _configuration["PAYPAL_CLIENT_ID"];
        var clientSecret = _configuration["PayPal:ClientSecret"] ?? _configuration["PAYPAL_CLIENT_SECRET"];

        return Task.FromResult((clientId, clientSecret));
    }

    /// <summary>
    /// Get Square credentials from environment variables
    /// </summary>
    public Task<(string? ApplicationId, string? AccessToken, string? LocationId)> GetSquareCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var applicationId = _configuration["Square:ApplicationId"] ?? _configuration["SQUARE_APPLICATION_ID"];
        var accessToken = _configuration["Square:AccessToken"] ?? _configuration["SQUARE_ACCESS_TOKEN"];
        var locationId = _configuration["Square:LocationId"] ?? _configuration["SQUARE_LOCATION_ID"];

        return Task.FromResult((applicationId, accessToken, locationId));
    }

    /// <summary>
    /// Get Redis connection string from environment variables
    /// </summary>
    public Task<string?> GetRedisConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration["Redis:ConnectionString"] ?? _configuration["REDIS_CONNECTION_STRING"];
        return Task.FromResult<string?>(connectionString);
    }

    /// <summary>
    /// Health check - always healthy since we're using environment variables
    /// </summary>
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
