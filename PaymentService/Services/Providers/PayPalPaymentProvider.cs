using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentService.Configuration;
using PaymentService.Models.DTOs;
using PaymentService.Models.Entities;
using PayPalCheckoutSdk.Core;
using PayPalCheckoutSdk.Orders;
using PayPalCheckoutSdk.Payments;

namespace PaymentService.Services.Providers;

/// <summary>
/// PayPal payment provider implementation
/// </summary>
public class PayPalPaymentProvider : IPaymentProvider
{
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentProvider> _logger;
    private readonly PayPalHttpClient _payPalClient;

    public string ProviderName => "paypal";
    
    public List<string> SupportedPaymentMethods => _settings.SupportedMethods;
    
    public bool IsEnabled => _settings.IsEnabled;

    public PayPalPaymentProvider(
        IOptions<PaymentProvidersSettings> settings,
        ILogger<PayPalPaymentProvider> logger)
    {
        _settings = settings.Value.PayPal;
        _logger = logger;
        
        // Initialize PayPal client
        PayPalEnvironment environment;
        if (_settings.IsSandbox)
        {
            environment = new SandboxEnvironment(_settings.ClientId, _settings.ClientSecret);
        }
        else
        {
            environment = new LiveEnvironment(_settings.ClientId, _settings.ClientSecret);
        }
        
        _payPalClient = new PayPalHttpClient(environment);

        if (!_settings.IsEnabled)
        {
            _logger.LogWarning("PayPal payment provider is disabled");
        }
    }

    public async Task<PaymentProviderResult> ProcessPaymentAsync(ProcessPaymentDto request, string correlationId)
    {
        try
        {
            _logger.LogInformation("Processing PayPal payment for order {OrderId}, amount {Amount} {Currency} [CorrelationId: {CorrelationId}]", 
                request.OrderId, request.Amount, request.Currency, correlationId);

            // Create PayPal order
            var orderRequest = BuildOrderRequest(request, correlationId);
            var createOrderRequest = new OrdersCreateRequest();
            createOrderRequest.Prefer("return=representation");
            createOrderRequest.RequestBody(orderRequest);

            var response = await _payPalClient.Execute(createOrderRequest);
            var order = response.Result<Order>();

            var result = new PaymentProviderResult();
            
            switch (order.Status)
            {
                case "CREATED":
                    result.IsSuccess = true;
                    result.Status = PaymentStatus.Pending;
                    result.ProviderTransactionId = order.Id;
                    result.Metadata = new Dictionary<string, object>
                    {
                        ["paypal_order_id"] = order.Id,
                        ["approval_url"] = order.Links?.FirstOrDefault(l => l.Rel == "approve")?.Href ?? "",
                        ["correlation_id"] = correlationId
                    };
                    
                    _logger.LogInformation("PayPal order created successfully with ID {OrderId} [CorrelationId: {CorrelationId}]", 
                        order.Id, correlationId);
                    break;
                    
                case "APPROVED":
                    // Capture the payment
                    var captureRequest = new OrdersCaptureRequest(order.Id);
                    var captureResponse = await _payPalClient.Execute(captureRequest);
                    var capturedOrder = captureResponse.Result<Order>();
                    
                    result.IsSuccess = true;
                    result.Status = PaymentStatus.Succeeded;
                    result.ProviderTransactionId = capturedOrder.Id;
                    result.Metadata = new Dictionary<string, object>
                    {
                        ["paypal_order_id"] = capturedOrder.Id,
                        ["correlation_id"] = correlationId
                    };
                    
                    _logger.LogInformation("PayPal payment captured successfully for order {OrderId} [CorrelationId: {CorrelationId}]", 
                        capturedOrder.Id, correlationId);
                    break;
                    
                default:
                    result.IsSuccess = false;
                    result.Status = PaymentStatus.Failed;
                    result.FailureReason = $"PayPal order status: {order.Status}";
                    
                    _logger.LogWarning("PayPal order in unexpected status {Status} for order {OrderId} [CorrelationId: {CorrelationId}]", 
                        order.Status, request.OrderId, correlationId);
                    break;
            }

            return result;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogError(ex, "PayPal HTTP error processing payment for order {OrderId} [CorrelationId: {CorrelationId}]", 
                request.OrderId, correlationId);
                
            return new PaymentProviderResult
            {
                IsSuccess = false,
                Status = PaymentStatus.Failed,
                FailureReason = "PayPal service unavailable"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PayPal payment for order {OrderId} [CorrelationId: {CorrelationId}]", 
                request.OrderId, correlationId);
                
            return new PaymentProviderResult
            {
                IsSuccess = false,
                Status = PaymentStatus.Failed,
                FailureReason = "Payment processing failed"
            };
        }
    }

    public async Task<RefundProviderResult> ProcessRefundAsync(Payment payment, decimal amount, string reason, string correlationId)
    {
        try
        {
            _logger.LogInformation("Processing PayPal refund for payment {PaymentId}, amount {Amount} [CorrelationId: {CorrelationId}]", 
                payment.Id, amount, correlationId);

            // PayPal refunds require the capture ID, which should be stored in the payment's metadata
            string? captureId = null;
            if (!string.IsNullOrEmpty(payment.Metadata))
            {
                try
                {
                    var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(payment.Metadata);
                    if (metadata?.TryGetValue("paypal_capture_id", out var captureIdObj) == true)
                    {
                        captureId = captureIdObj?.ToString();
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    _logger.LogWarning("Failed to deserialize payment metadata for PayPal refund");
                }
            }
            
            captureId ??= payment.ProviderTransactionId; // Fallback to transaction ID

            var refundRequest = new CapturesRefundRequest(captureId);
            refundRequest.RequestBody(new RefundRequest
            {
                Amount = new PayPalCheckoutSdk.Payments.Money
                {
                    Value = amount.ToString("F2"),
                    CurrencyCode = payment.Currency
                },
                NoteToPayer = reason
            });

            var response = await _payPalClient.Execute(refundRequest);
            var refund = response.Result<PayPalCheckoutSdk.Payments.Refund>();

            var result = new RefundProviderResult
            {
                RefundId = refund.Id,
                ProviderRefundId = refund.Id,
                Metadata = new Dictionary<string, object>
                {
                    ["paypal_refund_id"] = refund.Id,
                    ["correlation_id"] = correlationId
                }
            };

            switch (refund.Status)
            {
                case "COMPLETED":
                    result.IsSuccess = true;
                    result.Status = RefundStatus.Succeeded;
                    break;
                case "PENDING":
                    result.IsSuccess = true;
                    result.Status = RefundStatus.Processing;
                    break;
                default:
                    result.IsSuccess = false;
                    result.Status = RefundStatus.Failed;
                    result.FailureReason = $"PayPal refund status: {refund.Status}";
                    break;
            }

            _logger.LogInformation("PayPal refund processed with status {Status} for payment {PaymentId} [CorrelationId: {CorrelationId}]", 
                refund.Status, payment.Id, correlationId);

            return result;
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogError(ex, "PayPal HTTP error processing refund for payment {PaymentId} [CorrelationId: {CorrelationId}]", 
                payment.Id, correlationId);
                
            return new RefundProviderResult
            {
                IsSuccess = false,
                Status = RefundStatus.Failed,
                FailureReason = "PayPal service unavailable"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PayPal refund for payment {PaymentId} [CorrelationId: {CorrelationId}]", 
                payment.Id, correlationId);
                
            return new RefundProviderResult
            {
                IsSuccess = false,
                Status = RefundStatus.Failed,
                FailureReason = "Refund processing failed"
            };
        }
    }

    public Task<PaymentMethodResult> SavePaymentMethodAsync(SavePaymentMethodDto request, string correlationId)
    {
        _logger.LogInformation("PayPal does not support saving payment methods via API. Customer {CustomerId} [CorrelationId: {CorrelationId}]", 
            request.CustomerId, correlationId);

        // PayPal doesn't support saving payment methods in the same way as card providers
        // This would typically be handled through PayPal's billing agreements or reference transactions
        return Task.FromResult(new PaymentMethodResult
        {
            IsSuccess = false,
            FailureReason = "PayPal does not support saving payment methods. Use PayPal's billing agreements instead."
        });
    }

    public Task<bool> DeletePaymentMethodAsync(string providerTokenId, string correlationId)
    {
        _logger.LogInformation("PayPal does not support deleting payment methods via API. Token {TokenId} [CorrelationId: {CorrelationId}]", 
            providerTokenId, correlationId);

        // PayPal doesn't support deleting payment methods in the same way as card providers
        // This would typically be handled through PayPal's billing agreements
        return Task.FromResult(false);
    }

    private OrderRequest BuildOrderRequest(ProcessPaymentDto request, string correlationId)
    {
        var orderRequest = new OrderRequest
        {
            CheckoutPaymentIntent = "CAPTURE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = request.OrderId,
                    Description = request.Description ?? $"Payment for order {request.OrderId}",
                    CustomId = correlationId,
                    AmountWithBreakdown = new AmountWithBreakdown
                    {
                        CurrencyCode = request.Currency.ToUpper(),
                        Value = request.Amount.ToString("F2")
                    }
                }
            },
            ApplicationContext = new ApplicationContext
            {
                ReturnUrl = _settings.ReturnUrl,
                CancelUrl = _settings.CancelUrl,
                BrandName = "xshopai",
                LandingPage = "BILLING",
                UserAction = "PAY_NOW"
            }
        };

        return orderRequest;
    }
}
