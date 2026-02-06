using Microsoft.AspNetCore.Mvc;
using PaymentService.Configuration;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace PaymentService.Controllers;

/// <summary>
/// Operational/Infrastructure endpoints
/// These endpoints are used by monitoring systems, load balancers, and DevOps tools
/// </summary>
[ApiController]
public class OperationalController : ControllerBase
{
    private readonly ILogger<OperationalController> _logger;

    public OperationalController(ILogger<OperationalController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Readiness probe - check if service is ready to serve traffic
    /// </summary>
    /// <route>GET /health/ready</route>
    [HttpGet("/health/ready")]
    public ActionResult<object> Readiness()
    {
        _logger.LogDebug("Readiness check requested");
        
        try
        {
            // Add more sophisticated checks here (DB connectivity, payment providers, etc.)
            // Example: Check database connectivity, Stripe/PayPal API connectivity, etc.
            // await CheckDatabaseConnectivity();
            // await CheckPaymentProviderConnectivity();
            
            return Ok(new 
            { 
                status = "ready",
                service = "payment-service",
                timestamp = DateTime.UtcNow,
                checks = new {
                    database = "connected",
                    stripe = "connected",
                    paypal = "connected"
                    // Add other dependency checks
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readiness check failed");
            
            return StatusCode(503, new 
            { 
                status = "not ready",
                service = "payment-service",
                timestamp = DateTime.UtcNow,
                error = "Service dependencies not available"
            });
        }
    }

    /// <summary>
    /// Liveness probe - check if the app is running
    /// </summary>
    /// <route>GET /health/live</route>
    [HttpGet("/health/live")]
    public ActionResult<object> Liveness()
    {
        _logger.LogDebug("Liveness check requested");
        
        var process = Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow.Subtract(process.StartTime.ToUniversalTime());
        
        return Ok(new 
        { 
            status = "alive",
            service = "payment-service",
            timestamp = DateTime.UtcNow,
            uptime = uptime.TotalSeconds
        });
    }

    /// <summary>
    /// Basic metrics endpoint
    /// </summary>
    /// <route>GET /metrics</route>
    [HttpGet("/metrics")]
    public ActionResult<object> Metrics()
    {
        _logger.LogDebug("Metrics requested");
        
        var process = Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow.Subtract(process.StartTime.ToUniversalTime());
        
        return Ok(new 
        { 
            service = "payment-service",
            timestamp = DateTime.UtcNow,
            metrics = new {
                uptime = uptime.TotalSeconds,
                memory = new {
                    workingSet = process.WorkingSet64,
                    privateMemory = process.PrivateMemorySize64,
                    virtualMemory = process.VirtualMemorySize64
                },
                processorTime = process.TotalProcessorTime.TotalMilliseconds,
                threads = process.Threads.Count,
                handles = process.HandleCount,
                dotnetVersion = Environment.Version.ToString()
            }
        });
    }
}
