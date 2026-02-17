namespace PaymentService.Configuration;

/// <summary>
/// Simulation payment provider configuration for local development/testing
/// </summary>
public class SimulationSettings
{
    /// <summary>
    /// Whether the simulation provider is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Whether payments should automatically succeed
    /// Set to false to simulate failures
    /// </summary>
    public bool AutoSuccess { get; set; } = true;
    
    /// <summary>
    /// Simulated processing delay in milliseconds
    /// </summary>
    public int ProcessingDelayMs { get; set; } = 500;
    
    /// <summary>
    /// Supported payment methods for simulation
    /// </summary>
    public List<string> SupportedMethods { get; set; } = new() { "credit_card", "debit_card", "bank_transfer" };
}
