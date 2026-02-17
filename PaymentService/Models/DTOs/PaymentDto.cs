using System.Text.Json.Serialization;
using PaymentService.Models.Entities;

namespace PaymentService.Models.DTOs;

/// <summary>
/// Payment data transfer object for API responses
/// </summary>
public class PaymentDto
{
    public string Id { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    
    [JsonPropertyName("provider")]
    public string PaymentProvider { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; }
    public string? ProviderTransactionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? Description { get; set; }
    public string? FailureReason { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public List<RefundDto>? Refunds { get; set; }
}
