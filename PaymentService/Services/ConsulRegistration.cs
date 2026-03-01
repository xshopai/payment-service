using System.Text;
using System.Text.Json;

namespace PaymentService.Services;

/// <summary>
/// Consul self-registration hosted service.
/// Registers on startup, deregisters on shutdown.
/// Only active when CONSUL_URL environment variable is set.
/// </summary>
public class ConsulRegistrationService : IHostedService
{
    private readonly ILogger<ConsulRegistrationService> _logger;
    private readonly IConfiguration _configuration;
    private string _serviceId = string.Empty;
    private string _consulUrl = string.Empty;

    public ConsulRegistrationService(ILogger<ConsulRegistrationService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _consulUrl = Environment.GetEnvironmentVariable("CONSUL_URL") ?? string.Empty;
        if (string.IsNullOrEmpty(_consulUrl)) return;

        var serviceName = "payment-service";
        var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8009");
        var host = Environment.GetEnvironmentVariable("HOST") ?? "localhost";
        var address = host == "0.0.0.0" ? "localhost" : host;
        _serviceId = $"{serviceName}-{address}-{port}";

        var registration = new
        {
            ID = _serviceId,
            Name = serviceName,
            Address = address,
            Port = port,
            Check = new
            {
                HTTP = $"http://{address}:{port}/health",
                Interval = "10s",
                Timeout = "5s",
                DeregisterCriticalServiceAfter = "30s"
            }
        };

        try
        {
            using var client = new HttpClient();
            var json = JsonSerializer.Serialize(registration);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PutAsync($"{_consulUrl}/v1/agent/service/register", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Consul] Registered {ServiceName} ({ServiceId}) at {Address}:{Port}",
                    serviceName, _serviceId, address, port);
            }
            else
            {
                _logger.LogWarning("[Consul] Registration failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Consul] Registration failed (Consul unavailable): {Message}", ex.Message);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_consulUrl) || string.IsNullOrEmpty(_serviceId)) return;

        try
        {
            using var client = new HttpClient();
            await client.PutAsync($"{_consulUrl}/v1/agent/service/deregister/{_serviceId}", null, cancellationToken);
            _logger.LogInformation("[Consul] Deregistered {ServiceId}", _serviceId);
        }
        catch
        {
            // Best-effort — service is shutting down anyway
        }
    }
}
