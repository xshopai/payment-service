using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Events.Models;
using PaymentService.Models.DTOs;
using PaymentService.Services;
using PaymentService.Utils;

namespace PaymentService.Events.Consumers;

/// <summary>
/// Order Event Consumer
/// Handles order-related events from Dapr pub/sub
/// </summary>
[ApiController]
[Route("api/events/orders")]
public class OrderEventConsumer : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IStandardLogger _logger;
    private readonly IConfiguration _configuration;

    public OrderEventConsumer(
        IPaymentService paymentService,
        IStandardLogger logger,
        IConfiguration configuration)
    {
        _paymentService = paymentService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Handle order.created event
    /// Process payment when a new order is created
    /// </summary>
    [HttpPost("order-created")]
    public async Task<IActionResult> HandleOrderCreated([FromBody] OrderCreatedEvent orderEvent)
    {
        var correlationId = orderEvent.CorrelationId ?? Guid.NewGuid().ToString();

        try
        {
            _logger.Info($"Received order.created event for order {orderEvent.OrderId}", correlationId, new
            {
                operation = "ORDER_CREATED_EVENT",
                orderId = orderEvent.OrderId,
                customerId = orderEvent.CustomerId,
                amount = orderEvent.TotalAmount,
                currency = orderEvent.Currency,
                paymentMethod = orderEvent.PaymentMethod
            });

            // Check if payment already exists for this order
            var existingPayments = await _paymentService.GetPaymentsAsync(orderId: orderEvent.OrderId);
            
            if (existingPayments.Any(p => p.Status.ToString().ToLower() == "succeeded" || p.Status.ToString().ToLower() == "processing"))
            {
                _logger.Warn($"Payment already exists for order {orderEvent.OrderId}, skipping", correlationId, new
                {
                    operation = "ORDER_CREATED_EVENT_DUPLICATE",
                    orderId = orderEvent.OrderId,
                    existingPaymentCount = existingPayments.Count()
                });
                
                return Ok(new { message = "Payment already exists", skipped = true });
            }

            // Create payment request
            var paymentRequest = new ProcessPaymentDto
            {
                OrderId = orderEvent.OrderId,
                CustomerId = orderEvent.CustomerId,
                Amount = orderEvent.TotalAmount,
                Currency = orderEvent.Currency,
                PaymentMethod = orderEvent.PaymentMethod,
                Description = $"Payment for order {orderEvent.OrderId}",
                Metadata = orderEvent.Metadata ?? new Dictionary<string, object>()
            };

            // Add payment method ID to metadata if provided
            if (!string.IsNullOrEmpty(orderEvent.PaymentMethodId))
            {
                paymentRequest.Metadata["payment_method_id"] = orderEvent.PaymentMethodId;
            }

            // Process payment
            var paymentResult = await _paymentService.ProcessPaymentAsync(paymentRequest);

            _logger.Info($"Payment processed for order {orderEvent.OrderId}", correlationId, new
            {
                operation = "ORDER_CREATED_EVENT_PROCESSED",
                orderId = orderEvent.OrderId,
                paymentId = paymentResult.PaymentId,
                status = paymentResult.Status,
                success = paymentResult.IsSuccess
            });

            return Ok(new
            {
                success = paymentResult.IsSuccess,
                paymentId = paymentResult.PaymentId,
                status = paymentResult.Status.ToString(),
                error = paymentResult.ErrorMessage
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Error handling order.created event for order {orderEvent.OrderId}", ex, correlationId);

            // Return 200 to prevent Dapr from retrying
            // Log the error for investigation
            return Ok(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Handle order.cancelled event
    /// Process refund when an order is cancelled
    /// </summary>
    [HttpPost("order-cancelled")]
    public async Task<IActionResult> HandleOrderCancelled([FromBody] OrderCancelledEvent orderEvent)
    {
        var correlationId = orderEvent.CorrelationId ?? Guid.NewGuid().ToString();

        try
        {
            _logger.Info($"Received order.cancelled event for order {orderEvent.OrderId}", correlationId, new
            {
                operation = "ORDER_CANCELLED_EVENT",
                orderId = orderEvent.OrderId,
                customerId = orderEvent.CustomerId,
                reason = orderEvent.Reason,
                requiresRefund = orderEvent.RequiresRefund
            });

            // If no refund required, just log and return
            if (!orderEvent.RequiresRefund)
            {
                _logger.Info($"No refund required for cancelled order {orderEvent.OrderId}", correlationId, new
                {
                    operation = "ORDER_CANCELLED_NO_REFUND",
                    orderId = orderEvent.OrderId
                });

                return Ok(new { message = "No refund required", processed = false });
            }

            // Get payments for the order
            var payments = await _paymentService.GetPaymentsAsync(orderId: orderEvent.OrderId);
            var successfulPayment = payments.FirstOrDefault(p => p.Status.ToString().ToLower() == "succeeded");

            if (successfulPayment == null)
            {
                _logger.Warn($"No successful payment found for cancelled order {orderEvent.OrderId}", correlationId, new
                {
                    operation = "ORDER_CANCELLED_NO_PAYMENT",
                    orderId = orderEvent.OrderId,
                    paymentCount = payments.Count()
                });

                return Ok(new { message = "No successful payment found", processed = false });
            }

            // Process refund
            var refundAmount = orderEvent.RefundAmount ?? successfulPayment.Amount;
            var refundRequest = new ProcessRefundDto
            {
                PaymentId = successfulPayment.Id.ToString(),
                Amount = refundAmount,
                Reason = orderEvent.Reason ?? "Order cancelled"
            };

            var refundResult = await _paymentService.ProcessRefundAsync(refundRequest);

            _logger.Info($"Refund processed for cancelled order {orderEvent.OrderId}", correlationId, new
            {
                operation = "ORDER_CANCELLED_REFUND_PROCESSED",
                orderId = orderEvent.OrderId,
                paymentId = successfulPayment.Id,
                refundId = refundResult.RefundId,
                amount = refundAmount,
                status = refundResult.Status,
                success = refundResult.IsSuccess
            });

            return Ok(new
            {
                success = refundResult.IsSuccess,
                refundId = refundResult.RefundId,
                amount = refundAmount,
                status = refundResult.Status.ToString(),
                error = refundResult.ErrorMessage
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Error handling order.cancelled event for order {orderEvent.OrderId}", ex, correlationId);

            // Return 200 to prevent Dapr from retrying
            // Log the error for investigation
            return Ok(new { success = false, error = ex.Message });
        }
    }
}
