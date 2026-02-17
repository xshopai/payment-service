using Microsoft.Extensions.Options;
using PaymentService.Configuration;
using PaymentService.Services.Providers;

namespace PaymentService.Services;

/// <summary>
/// Factory for creating payment provider instances
/// </summary>
public class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PaymentProvidersSettings _settings;
    private readonly ILogger<PaymentProviderFactory> _logger;
    private readonly Dictionary<string, Func<IPaymentProvider>> _providerFactories;

    public PaymentProviderFactory(
        IServiceProvider serviceProvider,
        IOptions<PaymentProvidersSettings> settings,
        ILogger<PaymentProviderFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;

        // Initialize provider factories
        _providerFactories = new Dictionary<string, Func<IPaymentProvider>>(StringComparer.OrdinalIgnoreCase)
        {
            ["stripe"] = () => _serviceProvider.GetRequiredService<StripePaymentProvider>(),
            ["paypal"] = () => _serviceProvider.GetRequiredService<PayPalPaymentProvider>(),
            ["simulation"] = () => _serviceProvider.GetRequiredService<SimulationPaymentProvider>(),
            // Square provider temporarily disabled due to SDK compatibility issues
            // ["square"] = () => _serviceProvider.GetRequiredService<SquarePaymentProvider>()
        };

        _logger.LogInformation("Payment provider factory initialized with {ProviderCount} providers", 
            _providerFactories.Count);
    }

    public IPaymentProvider GetProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name cannot be null or empty", nameof(providerName));
        }

        if (!_providerFactories.TryGetValue(providerName, out var factory))
        {
            var availableProviders = string.Join(", ", _providerFactories.Keys);
            throw new NotSupportedException(
                $"Payment provider '{providerName}' is not supported. " +
                $"Available providers: {availableProviders}");
        }

        var provider = factory();
        
        if (!provider.IsEnabled)
        {
            throw new InvalidOperationException(
                $"Payment provider '{providerName}' is not enabled. " +
                "Please check the configuration.");
        }

        _logger.LogDebug("Retrieved payment provider: {ProviderName}", providerName);
        return provider;
    }

    public IPaymentProvider GetDefaultProvider()
    {
        var defaultProviderName = _settings.DefaultProvider;
        
        if (string.IsNullOrWhiteSpace(defaultProviderName))
        {
            throw new InvalidOperationException(
                "No default payment provider configured. " +
                "Please set PaymentProviders:DefaultProvider in configuration.");
        }

        _logger.LogDebug("Using default payment provider: {ProviderName}", defaultProviderName);
        return GetProvider(defaultProviderName);
    }

    public List<IPaymentProvider> GetEnabledProviders()
    {
        var enabledProviders = new List<IPaymentProvider>();

        foreach (var (providerName, factory) in _providerFactories)
        {
            try
            {
                var provider = factory();
                if (provider.IsEnabled)
                {
                    enabledProviders.Add(provider);
                    _logger.LogDebug("Enabled provider found: {ProviderName}", providerName);
                }
                else
                {
                    _logger.LogDebug("Provider disabled: {ProviderName}", providerName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize provider {ProviderName}", providerName);
            }
        }

        _logger.LogInformation("Found {EnabledProviderCount} enabled payment providers", enabledProviders.Count);
        return enabledProviders;
    }

    public List<string> GetSupportedPaymentMethods(string providerName)
    {
        try
        {
            var provider = GetProvider(providerName);
            return provider.SupportedPaymentMethods;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get supported payment methods for provider {ProviderName}", providerName);
            return new List<string>();
        }
    }

    public Dictionary<string, List<string>> GetAllSupportedPaymentMethods()
    {
        var supportedMethods = new Dictionary<string, List<string>>();
        
        foreach (var provider in GetEnabledProviders())
        {
            supportedMethods[provider.ProviderName] = provider.SupportedPaymentMethods;
        }

        return supportedMethods;
    }

    public bool IsProviderSupported(string providerName)
    {
        return _providerFactories.ContainsKey(providerName);
    }

    public bool IsProviderEnabled(string providerName)
    {
        try
        {
            var provider = GetProvider(providerName);
            return provider.IsEnabled;
        }
        catch
        {
            return false;
        }
    }

    public IPaymentProvider GetProviderForPaymentMethod(string paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            throw new ArgumentException("Payment method cannot be null or empty", nameof(paymentMethod));
        }

        var enabledProviders = GetEnabledProviders();
        
        // Find the first provider that supports this payment method
        var provider = enabledProviders.FirstOrDefault(p => 
            p.SupportedPaymentMethods.Contains(paymentMethod, StringComparer.OrdinalIgnoreCase));

        if (provider == null)
        {
            var supportedMethods = string.Join(", ", 
                enabledProviders.SelectMany(p => p.SupportedPaymentMethods).Distinct());
                
            throw new NotSupportedException(
                $"No enabled provider supports payment method '{paymentMethod}'. " +
                $"Supported methods: {supportedMethods}");
        }

        _logger.LogDebug("Found provider {ProviderName} for payment method {PaymentMethod}", 
            provider.ProviderName, paymentMethod);
            
        return provider;
    }

    public List<string> GetAvailableProviders()
    {
        return _providerFactories.Keys.ToList();
    }
}
