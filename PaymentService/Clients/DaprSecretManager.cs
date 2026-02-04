using Dapr.Client;

namespace PaymentService.Clients;

/// <summary>
/// Dapr Secret Management Service
/// Provides secret management using Dapr's secret store building block with ENV fallback.
/// 
/// Priority Order:
/// 1. Dapr Secret Store (.dapr/secrets.json) - when running with Dapr
/// 2. Environment Variable (.env file or system) - when running without Dapr
/// 
/// Secret Naming Convention:
///   Local (.dapr/secrets.json): UPPER_SNAKE_CASE (e.g., JWT_SECRET)
///   Azure Key Vault: lower-kebab-case (e.g., jwt-secret)
///   The mapping is handled by Dapr component configuration in Azure.
/// </summary>
public class DaprSecretManager
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<DaprSecretManager> _logger;
    private const string SecretStoreName = "secretstore";

    public DaprSecretManager(DaprClient daprClient, ILogger<DaprSecretManager> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
        
        _logger.LogInformation("Secret manager initialized (store={SecretStore})", SecretStoreName);
    }

    /// <summary>
    /// Get secret from Dapr secret store
    /// </summary>
    /// <param name="secretName">Name of the secret to retrieve</param>
    /// <returns>Secret value as string</returns>
    /// <exception cref="KeyNotFoundException">If secret not found in Dapr store</exception>
    private async Task<string> GetSecretAsync(string secretName)
    {
        try
        {
            var secrets = await _daprClient.GetSecretAsync(SecretStoreName, secretName);
            
            if (secrets.TryGetValue(secretName, out var value) && !string.IsNullOrEmpty(value))
            {
                _logger.LogDebug("{SecretName} retrieved from Dapr secret store", secretName);
                return value;
            }
            
            throw new KeyNotFoundException($"Secret '{secretName}' not found in Dapr store");
        }
        catch (Exception ex) when (ex is not KeyNotFoundException)
        {
            _logger.LogDebug("Failed to get {SecretName} from Dapr: {Error}", secretName, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Get secret with Dapr first, ENV fallback
    /// 
    /// Priority:
    /// 1. Try Dapr secret store first
    /// 2. Fallback to environment variable (from .env file or system)
    /// </summary>
    /// <param name="secretName">Name of the secret to retrieve</param>
    /// <returns>Secret value as string</returns>
    /// <exception cref="Exception">If secret not found in either Dapr or ENV</exception>
    public async Task<string> GetSecretWithFallbackAsync(string secretName)
    {
        // Priority 1: Try Dapr secret store
        try
        {
            var value = await GetSecretAsync(secretName);
            _logger.LogDebug("{SecretName} retrieved from Dapr secret store", secretName);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("{SecretName} not in Dapr store, trying ENV variable: {Error}", 
                secretName, ex.Message);
        }

        // Priority 2: Fallback to environment variable (from .env file)
        var envValue = Environment.GetEnvironmentVariable(secretName);
        if (!string.IsNullOrEmpty(envValue))
        {
            _logger.LogDebug("{SecretName} retrieved from ENV variable", secretName);
            return envValue;
        }

        throw new Exception($"{secretName} not found in Dapr secret store or ENV variables");
    }

    /// <summary>
    /// Get database connection string from Dapr or ENV
    /// </summary>
    public async Task<string> GetDatabaseConnectionStringAsync()
    {
        return await GetSecretWithFallbackAsync("DATABASE_CONNECTION_STRING");
    }

    /// <summary>
    /// Get JWT secret from Dapr or ENV
    /// </summary>
    public async Task<string> GetJwtSecretAsync()
    {
        return await GetSecretWithFallbackAsync("JWT_SECRET");
    }

    /// <summary>
    /// Get RabbitMQ connection string from Dapr or ENV
    /// </summary>
    public async Task<string> GetRabbitMqConnectionStringAsync()
    {
        return await GetSecretWithFallbackAsync("RABBITMQ_CONNECTION_STRING");
    }

    /// <summary>
    /// Get Stripe API key from Dapr or ENV
    /// </summary>
    public async Task<string> GetStripeApiKeyAsync()
    {
        return await GetSecretWithFallbackAsync("STRIPE_API_KEY");
    }
}
