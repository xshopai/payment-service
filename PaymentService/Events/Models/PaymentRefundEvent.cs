using System.Text.Json.Serialization;

namespace PaymentService.Events.Models;

/// <summary>
/// Payment Refund Event
/// Saga compensation event published by order-processor-service when saga fails
/// </summary>
public class PaymentRefundEvent
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("paymentId")]
    public string PaymentId { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
