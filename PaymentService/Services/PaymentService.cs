using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using PaymentService.Models.DTOs;
using PaymentService.Models.Entities;
using PaymentService.Services.Providers;
using PaymentService.Utils;

namespace PaymentService.Services;

/// <summary>
/// Main payment service for processing payments, refunds, and managing payment methods
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly PaymentDbContext _dbContext;
    private readonly IPaymentProviderFactory _providerFactory;
    private readonly ICurrentUserService _currentUserService;
    private readonly IStandardLogger _logger;

    public PaymentService(
        PaymentDbContext dbContext,
        IPaymentProviderFactory providerFactory,
        ICurrentUserService currentUserService,
        IStandardLogger logger)
    {
        _dbContext = dbContext;
        _providerFactory = providerFactory;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<PaymentResultDto> ProcessPaymentAsync(ProcessPaymentDto request)
    {
        var correlationId = CorrelationIdHelper.GetCorrelationId();
        var currentUserId = _currentUserService.UserId;

        var stopwatch = _logger.OperationStart("PROCESS_PAYMENT", correlationId, new {
            operation = "PROCESS_PAYMENT",
            orderId = request.OrderId,
            customerId = request.CustomerId,
            amount = request.Amount,
            currency = request.Currency,
            paymentMethod = request.PaymentMethod,
            paymentProvider = request.PaymentProvider,
            userId = currentUserId
        });

        try
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(request.OrderId))
            {
                _logger.Warn("Payment validation failed: Order ID is required", correlationId, new {
                    operation = "PROCESS_PAYMENT",
                    validationError = "missing_order_id"
                });
                
                return new PaymentResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "Order ID is required"
                };
            }

            if (request.Amount <= 0)
            {
                _logger.Warn("Payment validation failed: Payment amount must be greater than zero", correlationId, new {
                    operation = "PROCESS_PAYMENT",
                    amount = request.Amount,
                    validationError = "invalid_amount"
                });
                
                return new PaymentResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "Payment amount must be greater than zero"
                };
            }

            // Check for existing payment (idempotency check)
            var existingPayment = await _dbContext.Payments
                .FirstOrDefaultAsync(p => p.OrderId == request.OrderId);

            if (existingPayment != null)
            {
                // Handle idempotency - return existing payment based on its status
                switch (existingPayment.Status)
                {
                    case PaymentStatus.Succeeded:
                    case PaymentStatus.PartiallyRefunded:
                    case PaymentStatus.FullyRefunded:
                        _logger.Info("Payment already exists and succeeded (idempotent response)", correlationId, new {
                            operation = "PROCESS_PAYMENT",
                            orderId = request.OrderId,
                            existingPaymentId = existingPayment.Id,
                            existingStatus = existingPayment.Status.ToString()
                        });
                        
                        return new PaymentResultDto
                        {
                            IsSuccess = true,
                            PaymentId = existingPayment.Id.ToString(),
                            TransactionId = existingPayment.TransactionId,
                            ProviderTransactionId = existingPayment.ProviderTransactionId,
                            Status = existingPayment.Status,
                            Amount = existingPayment.Amount,
                            Currency = existingPayment.Currency,
                            ProcessedAt = existingPayment.ProcessedAt
                        };

                    case PaymentStatus.Pending:
                    case PaymentStatus.Processing:
                        _logger.Info("Payment already in progress (idempotent response)", correlationId, new {
                            operation = "PROCESS_PAYMENT",
                            orderId = request.OrderId,
                            existingPaymentId = existingPayment.Id,
                            existingStatus = existingPayment.Status.ToString()
                        });
                        
                        return new PaymentResultDto
                        {
                            IsSuccess = true,
                            PaymentId = existingPayment.Id.ToString(),
                            TransactionId = existingPayment.TransactionId,
                            Status = existingPayment.Status,
                            Amount = existingPayment.Amount,
                            Currency = existingPayment.Currency
                        };

                    case PaymentStatus.Failed:
                    case PaymentStatus.Cancelled:
                        // Allow retry for failed/cancelled payments - delete old record and create new
                        _logger.Info("Retrying failed/cancelled payment", correlationId, new {
                            operation = "PROCESS_PAYMENT",
                            orderId = request.OrderId,
                            existingPaymentId = existingPayment.Id,
                            previousStatus = existingPayment.Status.ToString()
                        });
                        _dbContext.Payments.Remove(existingPayment);
                        await _dbContext.SaveChangesAsync();
                        break;
                }
            }

            // Get payment provider
            IPaymentProvider provider;
            if (!string.IsNullOrWhiteSpace(request.PaymentProvider))
            {
                provider = _providerFactory.GetProvider(request.PaymentProvider);
            }
            else if (!string.IsNullOrWhiteSpace(request.PaymentMethod))
            {
                provider = _providerFactory.GetProviderForPaymentMethod(request.PaymentMethod);
            }
            else
            {
                provider = _providerFactory.GetDefaultProvider();
            }

            _logger.Info("Payment provider selected", correlationId, new {
                operation = "PROCESS_PAYMENT",
                providerName = provider.ProviderName,
                paymentMethod = request.PaymentMethod
            });

            // Create payment record
            var payment = new Payment
            {
                OrderId = request.OrderId,
                CustomerId = request.CustomerId,
                Amount = request.Amount,
                Currency = request.Currency.ToUpper(),
                PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "credit_card" : request.PaymentMethod,
                Provider = provider.ProviderName,
                Status = PaymentStatus.Pending,
                Description = request.Description,
                CreatedBy = currentUserId,
                Metadata = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["correlation_id"] = correlationId,
                    ["user_id"] = currentUserId
                })
            };

            _dbContext.Payments.Add(payment);
            await _dbContext.SaveChangesAsync();

            _logger.Info("Payment record created", correlationId, new {
                operation = "PROCESS_PAYMENT",
                paymentId = payment.Id,
                providerName = provider.ProviderName,
                status = payment.Status
            });

            // Process payment with provider
            var providerResult = await provider.ProcessPaymentAsync(request, correlationId);

            // Update payment record with provider response
            payment.ProviderTransactionId = providerResult.ProviderTransactionId;
            payment.Status = providerResult.Status;
            payment.FailureReason = providerResult.FailureReason;
            
            // Merge metadata
            if (providerResult.Metadata != null)
            {
                var existingMetadata = string.IsNullOrEmpty(payment.Metadata) 
                    ? new Dictionary<string, object>() 
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(payment.Metadata) ?? new Dictionary<string, object>();
                    
                foreach (var kvp in providerResult.Metadata)
                {
                    existingMetadata[kvp.Key] = kvp.Value;
                }
                
                payment.Metadata = System.Text.Json.JsonSerializer.Serialize(existingMetadata);
            }

            payment.UpdatedAt = DateTime.UtcNow;
            payment.UpdatedBy = currentUserId;

            await _dbContext.SaveChangesAsync();

            _logger.OperationComplete("PROCESS_PAYMENT", stopwatch, correlationId, new {
                paymentId = payment.Id,
                providerTransactionId = payment.ProviderTransactionId,
                status = payment.Status,
                isSuccess = providerResult.IsSuccess,
                amount = payment.Amount,
                currency = payment.Currency
            });

            if (providerResult.IsSuccess)
            {
                _logger.Business("PAYMENT_PROCESSED", correlationId, new {
                    paymentId = payment.Id,
                    orderId = payment.OrderId,
                    customerId = payment.CustomerId,
                    amount = payment.Amount,
                    currency = payment.Currency,
                    provider = payment.Provider,
                    paymentMethod = payment.PaymentMethod
                });
            }

            return new PaymentResultDto
            {
                PaymentId = payment.Id.ToString(),
                TransactionId = providerResult.TransactionId,
                ProviderTransactionId = providerResult.ProviderTransactionId,
                Status = providerResult.Status,
                IsSuccess = providerResult.IsSuccess,
                ErrorMessage = providerResult.FailureReason,
                PaymentProvider = provider.ProviderName,
                Amount = payment.Amount,
                Currency = payment.Currency,
                ProcessedAt = payment.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.OperationFailed("PROCESS_PAYMENT", stopwatch, ex, correlationId, new {
                orderId = request.OrderId,
                customerId = request.CustomerId,
                amount = request.Amount
            });

            return new PaymentResultDto
            {
                IsSuccess = false,
                ErrorMessage = "An unexpected error occurred while processing the payment"
            };
        }
    }

    public async Task<RefundResultDto> ProcessRefundAsync(ProcessRefundDto request)
    {
        var correlationId = CorrelationIdHelper.GetCorrelationId();
        var currentUserId = _currentUserService.UserId;

        var stopwatch = _logger.OperationStart("PROCESS_REFUND", correlationId, new {
            operation = "PROCESS_REFUND",
            paymentId = request.PaymentId,
            refundAmount = request.Amount,
            reason = request.Reason,
            userId = currentUserId
        });

        try
        {
            // Get original payment
            var payment = await _dbContext.Payments
                .FirstOrDefaultAsync(p => p.Id.ToString() == request.PaymentId);

            if (payment == null)
            {
                _logger.Warn("Refund validation failed: Payment not found", correlationId, new {
                    operation = "PROCESS_REFUND",
                    paymentId = request.PaymentId,
                    validationError = "payment_not_found"
                });
                
                return new RefundResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "Payment not found"
                };
            }

            if (payment.Status != PaymentStatus.Succeeded)
            {
                _logger.Warn("Refund validation failed: Payment not successful", correlationId, new {
                    operation = "PROCESS_REFUND",
                    paymentId = request.PaymentId,
                    paymentStatus = payment.Status,
                    validationError = "payment_not_successful"
                });
                
                return new RefundResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "Only successful payments can be refunded"
                };
            }

            // Calculate refund amount
            var refundAmount = request.Amount;
            
            // Check refund amount is valid
            var totalRefunded = await _dbContext.PaymentRefunds
                .Where(r => r.PaymentId == payment.Id && r.Status == RefundStatus.Succeeded)
                .SumAsync(r => r.Amount);

            if (totalRefunded + refundAmount > payment.Amount)
            {
                _logger.Warn("Refund validation failed: Amount exceeds available balance", correlationId, new {
                    operation = "PROCESS_REFUND",
                    paymentId = request.PaymentId,
                    requestedAmount = refundAmount,
                    totalRefunded = totalRefunded,
                    paymentAmount = payment.Amount,
                    validationError = "amount_exceeds_balance"
                });
                
                return new RefundResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "Refund amount exceeds available balance"
                };
            }

            // Get payment provider
            var provider = _providerFactory.GetProvider(payment.Provider);

            _logger.Info("Processing refund with provider", correlationId, new {
                operation = "PROCESS_REFUND",
                paymentId = payment.Id,
                refundAmount = refundAmount,
                provider = payment.Provider
            });

            // Create refund record
            var refund = new PaymentRefund
            {
                PaymentId = payment.Id,
                Amount = refundAmount,
                Reason = request.Reason,
                Status = RefundStatus.Pending,
                CreatedBy = currentUserId,
                Metadata = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["correlation_id"] = correlationId,
                    ["user_id"] = currentUserId,
                    ["original_payment_id"] = payment.Id
                })
            };

            _dbContext.PaymentRefunds.Add(refund);
            await _dbContext.SaveChangesAsync();

            _logger.Info("Refund record created", correlationId, new {
                operation = "PROCESS_REFUND",
                refundId = refund.Id,
                paymentId = payment.Id,
                amount = refundAmount
            });

            // Process refund with provider
            var providerResult = await provider.ProcessRefundAsync(payment, refundAmount, request.Reason ?? "Refund requested", correlationId);

            // Update refund record with provider response
            refund.ProviderRefundId = providerResult.ProviderRefundId;
            refund.Status = providerResult.Status;
            refund.FailureReason = providerResult.FailureReason;
            
            // Merge metadata if available
            if (providerResult.Metadata != null)
            {
                var existingMetadata = string.IsNullOrEmpty(refund.Metadata) 
                    ? new Dictionary<string, object>() 
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(refund.Metadata) ?? new Dictionary<string, object>();
                    
                foreach (var kvp in providerResult.Metadata)
                {
                    existingMetadata[kvp.Key] = kvp.Value;
                }
                
                refund.Metadata = System.Text.Json.JsonSerializer.Serialize(existingMetadata);
            }

            refund.UpdatedAt = DateTime.UtcNow;
            refund.UpdatedBy = currentUserId;

            await _dbContext.SaveChangesAsync();

            _logger.OperationComplete("PROCESS_REFUND", stopwatch, correlationId, new {
                refundId = refund.Id,
                paymentId = payment.Id,
                amount = refundAmount,
                status = refund.Status,
                isSuccess = providerResult.IsSuccess
            });

            if (providerResult.IsSuccess)
            {
                _logger.Business("REFUND_PROCESSED", correlationId, new {
                    refundId = refund.Id,
                    paymentId = payment.Id,
                    orderId = payment.OrderId,
                    customerId = payment.CustomerId,
                    amount = refundAmount,
                    currency = payment.Currency,
                    provider = payment.Provider,
                    reason = request.Reason
                });
            }

            return new RefundResultDto
            {
                RefundId = refund.Id.ToString(),
                PaymentId = payment.Id.ToString(),
                Amount = refundAmount,
                Currency = payment.Currency,
                Status = refund.Status,
                IsSuccess = providerResult.IsSuccess,
                ErrorMessage = providerResult.FailureReason,
                ProcessedAt = refund.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.OperationFailed("PROCESS_REFUND", stopwatch, ex, correlationId, new {
                paymentId = request.PaymentId,
                refundAmount = request.Amount
            });

            return new RefundResultDto
            {
                IsSuccess = false,
                ErrorMessage = "An unexpected error occurred while processing the refund"
            };
        }
    }

    public async Task<SavePaymentMethodResultDto> SavePaymentMethodAsync(SavePaymentMethodDto request)
    {
        var correlationId = CorrelationIdHelper.GetCorrelationId();
        var currentUserId = _currentUserService.UserId;

        var stopwatch = _logger.OperationStart("SAVE_PAYMENT_METHOD", correlationId, new {
            operation = "SAVE_PAYMENT_METHOD",
            customerId = request.CustomerId,
            paymentMethodType = request.PaymentMethodType,
            paymentProvider = request.PaymentProvider,
            isDefault = request.IsDefault,
            userId = currentUserId
        });

        try
        {
            // Get payment provider
            var provider = !string.IsNullOrWhiteSpace(request.PaymentProvider)
                ? _providerFactory.GetProvider(request.PaymentProvider)
                : _providerFactory.GetDefaultProvider();

            _logger.Info("Saving payment method with provider", correlationId, new {
                operation = "SAVE_PAYMENT_METHOD",
                provider = provider.ProviderName,
                customerId = request.CustomerId
            });

            // Save payment method with provider
            var providerResult = await provider.SavePaymentMethodAsync(request, correlationId);

            if (!providerResult.IsSuccess)
            {
                _logger.Warn("Provider failed to save payment method", correlationId, new {
                    operation = "SAVE_PAYMENT_METHOD",
                    provider = provider.ProviderName,
                    customerId = request.CustomerId,
                    error = providerResult.FailureReason
                });
                
                return new SavePaymentMethodResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = providerResult.FailureReason
                };
            }

            // Create payment method record
            var paymentMethod = new PaymentMethod
            {
                CustomerId = request.CustomerId,
                Provider = provider.ProviderName,
                ProviderTokenId = providerResult.ProviderTokenId ?? string.Empty,
                Type = request.PaymentMethodType,
                Last4Digits = providerResult.Last4Digits,
                Brand = providerResult.Brand ?? string.Empty,
                ExpiryMonth = providerResult.ExpiryMonth,
                ExpiryYear = providerResult.ExpiryYear,
                IsDefault = request.IsDefault,
                CreatedBy = currentUserId,
                Metadata = System.Text.Json.JsonSerializer.Serialize(providerResult.Metadata ?? new Dictionary<string, object>())
            };

            // If this is set as default, unset other default methods for this customer
            if (request.IsDefault)
            {
                var existingDefaults = await _dbContext.PaymentMethods
                    .Where(pm => pm.CustomerId == request.CustomerId && pm.IsDefault)
                    .ToListAsync();

                foreach (var existingDefault in existingDefaults)
                {
                    existingDefault.IsDefault = false;
                    existingDefault.UpdatedAt = DateTime.UtcNow;
                    existingDefault.UpdatedBy = currentUserId;
                }
            }

            _dbContext.PaymentMethods.Add(paymentMethod);
            await _dbContext.SaveChangesAsync();

            _logger.OperationComplete("SAVE_PAYMENT_METHOD", stopwatch, correlationId, new {
                paymentMethodId = paymentMethod.Id,
                customerId = request.CustomerId,
                provider = provider.ProviderName,
                isDefault = request.IsDefault
            });

            _logger.Business("PAYMENT_METHOD_SAVED", correlationId, new {
                paymentMethodId = paymentMethod.Id,
                customerId = request.CustomerId,
                provider = provider.ProviderName,
                paymentMethodType = request.PaymentMethodType
            });

            return new SavePaymentMethodResultDto
            {
                PaymentMethodId = paymentMethod.Id.ToString(),
                ProviderTokenId = paymentMethod.ProviderTokenId,
                Last4Digits = paymentMethod.Last4Digits,
                Brand = paymentMethod.Brand,
                ExpiryMonth = paymentMethod.ExpiryMonth,
                ExpiryYear = paymentMethod.ExpiryYear,
                IsDefault = paymentMethod.IsDefault,
                IsSuccess = true
            };
        }
        catch (Exception ex)
        {
            _logger.OperationFailed("SAVE_PAYMENT_METHOD", stopwatch, ex, correlationId, new {
                customerId = request.CustomerId,
                paymentMethodType = request.PaymentMethodType
            });

            return new SavePaymentMethodResultDto
            {
                IsSuccess = false,
                ErrorMessage = "An unexpected error occurred while saving the payment method"
            };
        }
    }

    public async Task<bool> DeletePaymentMethodAsync(Guid paymentMethodId)
    {
        var correlationId = CorrelationIdHelper.GetCorrelationId();
        var currentUserId = _currentUserService.UserId;

        _logger.Info("Deleting payment method", correlationId);

        try
        {
            var paymentMethod = await _dbContext.PaymentMethods
                .FirstOrDefaultAsync(pm => pm.Id == paymentMethodId);

            if (paymentMethod == null)
            {
                _logger.Warn("Payment method not found", correlationId);
                return false;
            }

            // Delete from provider
            var provider = _providerFactory.GetProvider(paymentMethod.Provider);
            var providerDeleted = await provider.DeletePaymentMethodAsync(paymentMethod.ProviderTokenId, correlationId);

            if (!providerDeleted)
            {
                _logger.Warn("Failed to delete payment method from provider", correlationId);
            }

            // Delete from database regardless of provider response
            _dbContext.PaymentMethods.Remove(paymentMethod);
            await _dbContext.SaveChangesAsync();

            _logger.Info("Payment method deleted", correlationId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Error deleting payment method", ex, correlationId);
            return false;
        }
    }

    public async Task<List<PaymentMethodDto>> GetPaymentMethodsAsync(string customerId)
    {
        var correlationId = CorrelationIdHelper.GetCorrelationId();

        _logger.Info("Getting payment methods for customer", correlationId);

        try
        {
            var paymentMethods = await _dbContext.PaymentMethods
                .Where(pm => pm.CustomerId == customerId)
                .OrderByDescending(pm => pm.IsDefault)
                .ThenByDescending(pm => pm.CreatedAt)
                .Select(pm => new PaymentMethodDto
                {
                    Id = pm.Id.ToString(),
                    PaymentProvider = pm.Provider,
                    PaymentMethodType = pm.Type,
                    Last4Digits = pm.Last4Digits,
                    Brand = pm.Brand,
                    ExpiryMonth = pm.ExpiryMonth,
                    ExpiryYear = pm.ExpiryYear,
                    IsDefault = pm.IsDefault,
                    CreatedAt = pm.CreatedAt
                })
                .ToListAsync();

            _logger.Info("Found payment methods for customer", correlationId);

            return paymentMethods;
        }
        catch (Exception ex)
        {
            _logger.Error("Error getting payment methods for customer", ex, correlationId);
            return new List<PaymentMethodDto>();
        }
    }

    public async Task<PaymentDto?> GetPaymentAsync(Guid paymentId)
    {
        var correlationId = CorrelationIdHelper.GetCorrelationId();

        _logger.Info("Getting payment", correlationId);

        try
        {
            var payment = await _dbContext.Payments
                .Include(p => p.Refunds)
                .FirstOrDefaultAsync(p => p.Id == paymentId);

            if (payment == null)
            {
                _logger.Warn("Payment not found", correlationId);
                return null;
            }

            return new PaymentDto
            {
                Id = payment.Id.ToString(),
                OrderId = payment.OrderId,
                CustomerId = payment.CustomerId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                PaymentMethod = payment.PaymentMethod,
                PaymentProvider = payment.Provider,
                Status = payment.Status,
                ProviderTransactionId = payment.ProviderTransactionId,
                FailureReason = payment.FailureReason,
                Description = payment.Description,
                CreatedAt = payment.CreatedAt,
                Refunds = payment.Refunds.Select(r => new RefundDto
                {
                    Id = r.Id.ToString(),
                    Amount = r.Amount,
                    Currency = r.Currency,
                    Status = r.Status,
                    Reason = r.Reason,
                    ProviderRefundId = r.ProviderRefundId,
                    FailureReason = r.FailureReason,
                    CreatedAt = r.CreatedAt
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Error getting payment", ex, correlationId);
            return null;
        }
    }

    public async Task<List<PaymentDto>> GetPaymentsAsync(string? customerId = null, string? orderId = null, int skip = 0, int take = 50)
    {
        var correlationId = CorrelationIdHelper.GetCorrelationId();

        _logger.Info("Getting payments", correlationId);

        try
        {
            var query = _dbContext.Payments.Include(p => p.Refunds).AsQueryable();

            if (!string.IsNullOrWhiteSpace(customerId))
            {
                query = query.Where(p => p.CustomerId == customerId);
            }

            if (!string.IsNullOrWhiteSpace(orderId))
            {
                query = query.Where(p => p.OrderId == orderId);
            }

            var payments = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(p => new PaymentDto
                {
                    Id = p.Id.ToString(),
                    OrderId = p.OrderId,
                    CustomerId = p.CustomerId,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    PaymentMethod = p.PaymentMethod,
                    PaymentProvider = p.Provider,
                    Status = p.Status,
                    ProviderTransactionId = p.ProviderTransactionId,
                    FailureReason = p.FailureReason,
                    Description = p.Description,
                    CreatedAt = p.CreatedAt,
                    Refunds = p.Refunds.Select(r => new RefundDto
                    {
                        Id = r.Id.ToString(),
                        Amount = r.Amount,
                        Currency = r.Currency,
                        Status = r.Status,
                        Reason = r.Reason,
                        ProviderRefundId = r.ProviderRefundId,
                        FailureReason = r.FailureReason,
                        CreatedAt = r.CreatedAt
                    }).ToList()
                })
                .ToListAsync();

            _logger.Info("Found payments", correlationId);

            return payments;
        }
        catch (Exception ex)
        {
            _logger.Error("Error getting payments", ex, correlationId);
            return new List<PaymentDto>();
        }
    }
}
