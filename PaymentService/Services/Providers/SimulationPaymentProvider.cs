using Microsoft.Extensions.Options;
using PaymentService.Configuration;
using PaymentService.Models.DTOs;
using PaymentService.Models.Entities;

namespace PaymentService.Services.Providers;

/// <summary>
/// Simulation payment provider for local development/testing
/// Always succeeds without calling any external payment gateway
/// </summary>
public class SimulationPaymentProvider : IPaymentProvider
{
    private readonly SimulationSettings _settings;
    private readonly ILogger<SimulationPaymentProvider> _logger;

    public string ProviderName => "simulation";
    public List<string> SupportedPaymentMethods => new() { "credit_card", "debit_card", "bank_transfer" };
    public bool IsEnabled => _settings.IsEnabled;

    public SimulationPaymentProvider(
        IOptions<PaymentProvidersSettings> paymentProvidersSettings,
        ILogger<SimulationPaymentProvider> logger)
    {
        _settings = paymentProvidersSettings.Value.Simulation;
        _logger = logger;

        _logger.LogInformation("Simulation payment provider initialized. Enabled: {IsEnabled}, AutoSuccess: {AutoSuccess}", 
            IsEnabled, _settings.AutoSuccess);
    }

    public async Task<PaymentProviderResult> ProcessPaymentAsync(ProcessPaymentDto request, string correlationId)
    {
        _logger.LogInformation("📝 [SIMULATION] Processing payment for order {OrderId}, Amount: {Amount} {Currency} [CorrelationId: {CorrelationId}]", 
            request.OrderId, request.Amount, request.Currency, correlationId);

        // Simulate processing delay
        await Task.Delay(_settings.ProcessingDelayMs);

        // Check if we should simulate failure
        if (!_settings.AutoSuccess || ShouldSimulateFailure(request))
        {
            var failureReason = GetSimulatedFailureReason(request);
            _logger.LogWarning("📝 [SIMULATION] Payment FAILED for order {OrderId}: {Reason}", 
                request.OrderId, failureReason);

            return new PaymentProviderResult
            {
                IsSuccess = false,
                TransactionId = $"sim_fail_{Guid.NewGuid():N}",
                Status = PaymentStatus.Failed,
                FailureReason = failureReason
            };
        }

        var transactionId = $"sim_txn_{Guid.NewGuid():N}";
        
        _logger.LogInformation("✅ [SIMULATION] Payment SUCCEEDED for order {OrderId}, TransactionId: {TransactionId}", 
            request.OrderId, transactionId);

        return new PaymentProviderResult
        {
            IsSuccess = true,
            TransactionId = transactionId,
            ProviderTransactionId = $"sim_pi_{Guid.NewGuid():N}",
            Status = PaymentStatus.Succeeded,
            Metadata = new Dictionary<string, object>
            {
                ["provider"] = "simulation",
                ["mode"] = "auto_success",
                ["order_id"] = request.OrderId,
                ["correlation_id"] = correlationId
            }
        };
    }

    public async Task<RefundProviderResult> ProcessRefundAsync(Payment payment, decimal amount, string reason, string correlationId)
    {
        _logger.LogInformation("📝 [SIMULATION] Processing refund for payment {PaymentId}, Amount: {Amount} [CorrelationId: {CorrelationId}]", 
            payment.Id, amount, correlationId);

        await Task.Delay(_settings.ProcessingDelayMs);

        var refundId = $"sim_refund_{Guid.NewGuid():N}";

        _logger.LogInformation("✅ [SIMULATION] Refund SUCCEEDED: {RefundId}", refundId);

        return new RefundProviderResult
        {
            IsSuccess = true,
            RefundId = refundId,
            ProviderRefundId = $"sim_re_{Guid.NewGuid():N}",
            Status = RefundStatus.Succeeded
        };
    }

    public async Task<PaymentMethodResult> SavePaymentMethodAsync(SavePaymentMethodDto request, string correlationId)
    {
        _logger.LogInformation("📝 [SIMULATION] Saving payment method for customer {CustomerId}", request.CustomerId);

        await Task.Delay(100);

        return new PaymentMethodResult
        {
            IsSuccess = true,
            ProviderTokenId = $"sim_pm_{Guid.NewGuid():N}",
            Last4Digits = request.CardLast4 ?? "4242",
            Brand = request.CardBrand ?? "visa",
            ExpiryMonth = request.CardExpiryMonth ?? 12,
            ExpiryYear = request.CardExpiryYear ?? 2030,
            Metadata = new Dictionary<string, object>
            {
                ["provider"] = "simulation"
            }
        };
    }

    public async Task<bool> DeletePaymentMethodAsync(string providerTokenId, string correlationId)
    {
        _logger.LogInformation("📝 [SIMULATION] Deleting payment method {TokenId}", providerTokenId);
        await Task.Delay(50);
        return true;
    }

    private bool ShouldSimulateFailure(ProcessPaymentDto request)
    {
        // Simulate failure for specific test amounts
        // Amount ending in .99 triggers failure for testing
        return request.Amount % 1 == 0.99m;
    }

    private string GetSimulatedFailureReason(ProcessPaymentDto request)
    {
        if (request.Amount % 1 == 0.99m)
            return "Simulated failure: Test amount ending in .99";
        
        return "Simulated failure: AutoSuccess is disabled";
    }
}
