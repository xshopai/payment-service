using Dapr.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PaymentService.Data;
using System.Text;

namespace PaymentService.Configuration;

/// <summary>
/// Configuration provider that loads secrets from Dapr Secret Store at startup
/// </summary>
public static class DaprSecretsConfiguration
{
    private const string SecretStoreName = "secretstore";

    /// <summary>
    /// Load secrets from Dapr and configure IConfiguration
    /// Call this early in Program.cs before other services are configured
    /// </summary>
    public static async Task LoadDaprSecretsAsync(IServiceCollection services, IConfiguration configuration)
    {
        var serviceProvider = services.BuildServiceProvider();
        var daprClient = serviceProvider.GetRequiredService<DaprClient>();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Retry logic to wait for Dapr sidecar to initialize
        const int maxRetries = 10;
        const int delayMs = 1000;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                logger.LogInformation("Loading secrets from Dapr Secret Store: {StoreName} (attempt {Attempt}/{MaxRetries})", 
                    SecretStoreName, attempt, maxRetries);
                
                // Try to load at least one critical secret to verify store is ready
                var jwtSecrets = await daprClient.GetSecretAsync(SecretStoreName, "Jwt");
                
                // If we get here, the secret store is initialized - now load all secrets
                await LoadAllSecretsAsync(daprClient, configuration, logger);
                logger.LogInformation("Dapr secrets loaded successfully");
                return;
            }
            catch (Exception ex) when (attempt < maxRetries && 
                (ex.Message.Contains("secret store is not configured") || 
                 ex.Message.Contains("FailedPrecondition")))
            {
                logger.LogWarning("Dapr secret store not ready yet, retrying in {DelayMs}ms... (attempt {Attempt}/{MaxRetries})", 
                    delayMs, attempt, maxRetries);
                await Task.Delay(delayMs);
            }
        }
        
        throw new InvalidOperationException(
            $"Failed to connect to Dapr secret store '{SecretStoreName}' after {maxRetries} attempts. " +
            "Ensure Dapr sidecar is running and secret store component is configured.");
    }

    private static async Task LoadAllSecretsAsync(DaprClient daprClient, IConfiguration configuration, ILogger logger)
    {
        // Load JWT secrets
        try
        {
            var jwtSecrets = await daprClient.GetSecretAsync(SecretStoreName, "Jwt");
            if (jwtSecrets.ContainsKey("Key"))
            {
                configuration["Jwt:Key"] = jwtSecrets["Key"];
            }
            if (jwtSecrets.ContainsKey("Issuer"))
            {
                configuration["Jwt:Issuer"] = jwtSecrets["Issuer"];
            }
            if (jwtSecrets.ContainsKey("Audience"))
            {
                configuration["Jwt:Audience"] = jwtSecrets["Audience"];
            }
            logger.LogInformation("JWT secrets loaded from Dapr");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load JWT secrets from Dapr");
        }

        // Load database connection string
        try
        {
            var dbSecrets = await daprClient.GetSecretAsync(SecretStoreName, "ConnectionStrings");
            if (dbSecrets.ContainsKey("DefaultConnection"))
            {
                configuration["ConnectionStrings:DefaultConnection"] = dbSecrets["DefaultConnection"];
                logger.LogInformation("Database connection string loaded from Dapr");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load database connection string from Dapr");
        }

        // Load payment provider credentials
        await LoadPaymentProviderSecretsAsync(daprClient, configuration, logger);

        // Load Redis connection string
        try
        {
            var redisSecrets = await daprClient.GetSecretAsync(SecretStoreName, "Redis");
            if (redisSecrets.ContainsKey("ConnectionString"))
            {
                configuration["Redis:ConnectionString"] = redisSecrets["ConnectionString"];
                logger.LogInformation("Redis connection string loaded from Dapr");
            }
            if (redisSecrets.ContainsKey("Password"))
            {
                configuration["Redis:Password"] = redisSecrets["Password"];
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load Redis secrets from Dapr");
        }
    }

    private static async Task LoadPaymentProviderSecretsAsync(DaprClient daprClient, IConfiguration configuration, ILogger logger)
    {
        // Load Stripe credentials
        try
        {
            var stripeSecrets = await daprClient.GetSecretAsync(SecretStoreName, "Stripe");
            if (stripeSecrets.ContainsKey("PublishableKey"))
            {
                configuration["PaymentProviders:Stripe:PublishableKey"] = stripeSecrets["PublishableKey"];
            }
            if (stripeSecrets.ContainsKey("SecretKey"))
            {
                configuration["PaymentProviders:Stripe:SecretKey"] = stripeSecrets["SecretKey"];
            }
            if (stripeSecrets.ContainsKey("WebhookSecret"))
            {
                configuration["PaymentProviders:Stripe:WebhookSecret"] = stripeSecrets["WebhookSecret"];
            }
            logger.LogInformation("Stripe credentials loaded from Dapr");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load Stripe secrets from Dapr");
        }

        // Load PayPal credentials
        try
        {
            var paypalSecrets = await daprClient.GetSecretAsync(SecretStoreName, "PayPal");
            if (paypalSecrets.ContainsKey("ClientId"))
            {
                configuration["PaymentProviders:PayPal:ClientId"] = paypalSecrets["ClientId"];
            }
            if (paypalSecrets.ContainsKey("ClientSecret"))
            {
                configuration["PaymentProviders:PayPal:ClientSecret"] = paypalSecrets["ClientSecret"];
            }
            logger.LogInformation("PayPal credentials loaded from Dapr");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load PayPal secrets from Dapr");
        }

        // Load Square credentials
        try
        {
            var squareSecrets = await daprClient.GetSecretAsync(SecretStoreName, "Square");
            if (squareSecrets.ContainsKey("ApplicationId"))
            {
                configuration["PaymentProviders:Square:ApplicationId"] = squareSecrets["ApplicationId"];
            }
            if (squareSecrets.ContainsKey("AccessToken"))
            {
                configuration["PaymentProviders:Square:AccessToken"] = squareSecrets["AccessToken"];
            }
            if (squareSecrets.ContainsKey("LocationId"))
            {
                configuration["PaymentProviders:Square:LocationId"] = squareSecrets["LocationId"];
            }
            logger.LogInformation("Square credentials loaded from Dapr");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load Square secrets from Dapr");
        }
    }
}

