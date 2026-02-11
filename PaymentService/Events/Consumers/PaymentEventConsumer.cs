using Microsoft.AspNetCore.Mvc;
using PaymentService.Events.Models;
using PaymentService.Services;
using PaymentService.Utils;

namespace PaymentService.Events.Consumers;

/// <summary>
/// Payment Event Consumer
/// Handles payment-related compensation events from Dapr pub/sub
/// </summary>
[ApiController]
[Route("api/events/payments")]
public class PaymentEventConsumer : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IStandardLogger _logger;

    public PaymentEventConsumer(
        IPaymentService paymentService,
        IStandardLogger logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>
    /// Handle payment.refund compensation event
    /// 
    /// CRITICAL SAGA COMPENSATION TRANSACTION
    /// Triggered by order-processor-service when order saga fails after payment succeeded.
    /// Prevents financial data corruption by refunding charged customers.
    /// 
    /// Scenarios:
    /// - Inventory reservation fails after payment → Refund payment
    /// - Shipping preparation fails after payment → Refund payment
    /// - Order cancelled by admin/user after payment → Refund payment
    /// 
    /// Idempotent: Multiple calls with same paymentId won't duplicate refunds
    /// </summary>
    [HttpPost("refund")]
    public async Task<IActionResult> HandlePaymentRefund([FromBody] PaymentRefundEvent refundEvent)
    {
        var correlationId = refundEvent.CorrelationId ?? Guid.NewGuid().ToString();

        try
        {
            _logger.Info(
                $"🔄 SAGA COMPENSATION: Received payment.refund event for order {refundEvent.OrderId}",
                correlationId,
                new
                {
                    operation = "PAYMENT_REFUND_COMPENSATION",
                    orderId = refundEvent.OrderId,
                    paymentId = refundEvent.PaymentId,
                    sagaCompensation = true
                });

            // Validate required fields
            if (string.IsNullOrEmpty(refundEvent.PaymentId))
            {
                _logger.Warn(
                    $"⚠️ SAGA COMPENSATION: payment.refund event missing paymentId for order {refundEvent.OrderId}",
                    correlationId,
                    new { orderId = refundEvent.OrderId });
                
                return BadRequest(new { message = "Missing paymentId in refund event" });
            }

            // Find the payment to refund
            var payment = await _paymentService.GetPaymentAsync(Guid.Parse(refundEvent.PaymentId));
            
            if (payment == null)
            {
                _logger.Warn(
                    $"⚠️ SAGA COMPENSATION: Payment not found for refund: {refundEvent.PaymentId}",
                    correlationId,
                    new { paymentId = refundEvent.PaymentId, orderId = refundEvent.OrderId });
                
                // Return OK - payment may have been already refunded or never existed
                // Compensation event handling should be idempotent
                return Ok(new { message = "Payment not found (may be already refunded)", skipped = true });
            }

            // Check if payment is in refundable state
            var status = payment.Status.ToString().ToLower();
            if (status == "refunded" || status == "cancelled")
            {
                _logger.Info(
                    $"✅ SAGA COMPENSATION: Payment already refunded/cancelled: {refundEvent.PaymentId}",
                    correlationId,
                    new {
                        paymentId = refundEvent.PaymentId,
                        orderId = refundEvent.OrderId,
                        currentStatus = payment.Status,
                        idempotent = true
                    });
                
                // Idempotent - already compensated, return success
                return Ok(new
                {
                    success = true,
                    message = "Payment already refunded",
                    paymentId = refundEvent.PaymentId,
                    status = payment.Status,
                    alreadyProcessed = true
                });
            }

            // Only process refund if payment succeeded (can't refund failed/pending payments)
            if (status != "succeeded" && status != "processing")
            {
                _logger.Warn(
                    $"⚠️ SAGA COMPENSATION: Payment not in refundable state: {refundEvent.PaymentId} (status: {payment.Status})",
                    correlationId,
                    new { paymentId = refundEvent.PaymentId, orderId = refundEvent.OrderId, status = payment.Status });
                
                return Ok(new
                {
                    success = true,
                    message = $"Payment in non-refundable state: {payment.Status}",
                    paymentId = refundEvent.PaymentId,
                    skipped = true
                });
            }

            // Process refund (saga compensation)
            var refundResult = await _paymentService.ProcessRefundAsync(
                new PaymentService.Models.DTOs.ProcessRefundDto
                {
                    PaymentId = refundEvent.PaymentId,
                    Amount = payment.Amount,
                    Reason = $"SAGA_COMPENSATION: Order saga failed: {refundEvent.Reason ?? "Unknown reason"}"
                });

            if (refundResult.IsSuccess)
            {
                _logger.Info(
                    $"✅ SAGA COMPENSATION COMPLETE: Payment refunded for order {refundEvent.OrderId}",
                    correlationId,
                    new {
                        operation = "PAYMENT_REFUND_SUCCESS",
                        orderId = refundEvent.OrderId,
                        paymentId = refundEvent.PaymentId,
                        refundId = refundResult.RefundId,
                        amount = payment.Amount,
                        currency = payment.Currency,
                        sagaCompensation = true
                    });

                return Ok(new
                {
                    success = true,
                    message = "Saga compensation completed: Payment refunded",
                    paymentId = refundEvent.PaymentId,
                    refundId = refundResult.RefundId,
                    orderId = refundEvent.OrderId,
                    amount = payment.Amount,
                    currency = payment.Currency
                });
            }
            else
            {
                _logger.Error(
                    $"❌ SAGA COMPENSATION FAILED: Could not refund payment for order {refundEvent.OrderId}",
                    null,
                    correlationId,
                    new {
                        operation = "PAYMENT_REFUND_FAILED",
                        orderId = refundEvent.OrderId,
                        paymentId = refundEvent.PaymentId,
                        error = refundResult.ErrorMessage,
                        sagaCompensation = true
                    });

                // Return 500 to trigger Dapr retry - compensation MUST succeed
                return StatusCode(500, new
                {
                    success = false,
                    message = "Saga compensation failed: Refund failed",
                    error = refundResult.ErrorMessage,
                    paymentId = refundEvent.PaymentId,
                    orderId = refundEvent.OrderId,
                    shouldRetry = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"❌ SAGA COMPENSATION ERROR: Exception during payment refund for order {refundEvent.OrderId}",
                ex,
                correlationId,
                new {
                    operation = "PAYMENT_REFUND_EXCEPTION",
                    orderId = refundEvent.OrderId,
                    paymentId = refundEvent.PaymentId,
                    error = ex.Message,
                    stackTrace = ex.StackTrace,
                    sagaCompensation = true
                });

            // Return 500 to trigger Dapr retry - compensation MUST succeed
            return StatusCode(500, new
            {
                success = false,
                message = "Saga compensation exception",
                error = ex.Message,
                orderId = refundEvent.OrderId,
                shouldRetry = true
            });
        }
    }
}
