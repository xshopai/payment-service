namespace PaymentService.Configuration;

/// <summary>
/// Payment provider configuration
/// </summary>
public class PaymentProvidersSettings
{
    public const string SectionName = "PaymentProviders";
    
    public string DefaultProvider { get; set; } = "stripe";
    public StripeSettings Stripe { get; set; } = new();
    public PayPalSettings PayPal { get; set; } = new();
    public SquareSettings Square { get; set; } = new();
    public SimulationSettings Simulation { get; set; } = new();
}
