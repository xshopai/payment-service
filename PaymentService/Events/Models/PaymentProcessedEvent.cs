using System.Text.Json.Serialization;

namespace PaymentService.Events.Models;

/// <summary>
/// Payment Processed Event
/// Published when payment processing succeeds
/// Consumed by order-processor-service to advance the saga
/// </summary>
public class PaymentProcessedEvent
{
    [JsonPropertyName("orderId")]
    public Guid OrderId { get; set; }

    [JsonPropertyName("paymentId")]
    public string PaymentId { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("processedAt")]
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Payment Failed Event
/// Published when payment processing fails
/// Consumed by order-processor-service to handle saga failure
/// </summary>
public class PaymentFailedEvent
{
    [JsonPropertyName("orderId")]
    public Guid OrderId { get; set; }

    [JsonPropertyName("paymentId")]
    public string? PaymentId { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("failedAt")]
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;
}
