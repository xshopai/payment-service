namespace PaymentService.Events.Models;

/// <summary>
/// Event payload for order.created event
/// </summary>
public class OrderCreatedEvent
{
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string PaymentMethod { get; set; } = string.Empty;
    public string? PaymentMethodId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Event payload for order.cancelled event
/// </summary>
public class OrderCancelledEvent
{
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string? PaymentId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CancelledBy { get; set; } = string.Empty;
    public bool RequiresRefund { get; set; }
    public decimal? RefundAmount { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CancelledAt { get; set; }
}
