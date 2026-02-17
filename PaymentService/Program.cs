using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PaymentService.Configuration;
using PaymentService.Data;
using PaymentService.Messaging;
using PaymentService.Events.Publishers;
using PaymentService.Middlewares;
using PaymentService.Services;
using PaymentService.Services.Providers;
using PaymentService.Utils;
using System.Text;
using System.Text.Json.Serialization;
using StripeProvider = PaymentService.Services.Providers.StripePaymentProvider;
using PayPalProvider = PaymentService.Services.Providers.PayPalPaymentProvider;
using SimulationProvider = PaymentService.Services.Providers.SimulationPaymentProvider;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Azure.Monitor.OpenTelemetry.Exporter;

var builder = WebApplication.CreateBuilder(args);

// Port configuration is handled via ASPNETCORE_URLS environment variable
// Default: http://+:8009 (set in Dockerfile or container environment)

// Add Dapr client for runtime secret access
builder.Services.AddDaprClient();

// Add services to the container.
builder.Services.AddControllers()
    .AddDapr() // Add Dapr integration
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase; // Use camelCase for JavaScript clients
    });

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "Payment Service API", 
        Version = "v1",
        Description = "Payment processing service with support for multiple payment providers (Stripe, PayPal, Square)"
    });
    
    // Add JWT authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// Configuration
builder.Services.Configure<PaymentProvidersSettings>(
    builder.Configuration.GetSection("PaymentProviders"));

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Database connection string is required. Set ConnectionStrings:DefaultConnection.");
}

builder.Services.AddDbContext<PaymentDbContext>(options =>
{
    options.UseSqlServer(
        connectionString,
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null));
});

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

if (string.IsNullOrEmpty(jwtKey))
{
    throw new InvalidOperationException("JWT Key is required. Set Jwt:Key in configuration.");
}

// Use UTF8 encoding to match other services (auth-service, order-service)
var key = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = !string.IsNullOrEmpty(jwtIssuer),
            ValidIssuer = jwtIssuer,
            ValidateAudience = !string.IsNullOrEmpty(jwtAudience),
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        
        // Add JWT event handlers for debugging
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"❌ JWT Authentication failed: {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var claims = context.Principal?.Claims?.Select(c => $"{c.Type}={c.Value}");
                Console.WriteLine($"✅ JWT Token validated. Claims: {string.Join(", ", claims ?? Array.Empty<string>())}");
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                Console.WriteLine($"⚠️ JWT Challenge: Error={context.Error}, ErrorDescription={context.ErrorDescription}");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// HTTP Context Accessor
builder.Services.AddHttpContextAccessor();

// Application Services
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IStandardLogger, StandardLogger>();
builder.Services.AddScoped<IPaymentService, PaymentService.Services.PaymentService>();

// Dapr Services
builder.Services.AddSingleton<DaprEventPublisher>();

// Register Messaging abstraction layer (supports dapr, rabbitmq, servicebus via MESSAGING_PROVIDER config)
builder.Services.AddMessaging(builder.Configuration);

// Register RabbitMQ background consumer only when using direct RabbitMQ mode (not Dapr)
var messagingProvider = builder.Configuration["MESSAGING_PROVIDER"]
    ?? Environment.GetEnvironmentVariable("MESSAGING_PROVIDER")
    ?? "dapr";

if (messagingProvider.Equals("rabbitmq", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<PaymentService.Messaging.RabbitMQBackgroundConsumer>();
    Console.WriteLine("✅ RabbitMQ Background Consumer registered");
}

// Payment Providers
builder.Services.AddScoped<StripeProvider>();
builder.Services.AddScoped<PayPalProvider>();
builder.Services.AddScoped<SimulationProvider>();
// Square provider temporarily disabled due to SDK compatibility issues
// builder.Services.AddScoped<SquarePaymentProvider>();
builder.Services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PaymentDbContext>();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Configure OpenTelemetry tracing based on OTEL_TRACES_EXPORTER environment variable or config
// Supported values: zipkin, otlp, azure, none (default)
var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") 
    ?? builder.Configuration["Tracing:ServiceName"] 
    ?? "payment-service";
var tracesExporter = (Environment.GetEnvironmentVariable("OTEL_TRACES_EXPORTER") 
    ?? builder.Configuration["Tracing:Exporter"] 
    ?? "none").ToLower();

switch (tracesExporter)
{
    case "zipkin":
        var zipkinEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_ZIPKIN_ENDPOINT") 
            ?? builder.Configuration["Tracing:ZipkinEndpoint"] 
            ?? "http://localhost:9411/api/v2/spans";
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddZipkinExporter(options => options.Endpoint = new Uri(zipkinEndpoint)));
        Console.WriteLine($"✅ Tracing: Zipkin exporter → {zipkinEndpoint}");
        break;

    case "otlp":
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") 
            ?? builder.Configuration["Tracing:OtlpEndpoint"] 
            ?? "http://localhost:4318";
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)));
        Console.WriteLine($"✅ Tracing: OTLP exporter → {otlpEndpoint}");
        break;

    case "azure":
        var appInsightsConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(serviceName))
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddAzureMonitorTraceExporter(options => options.ConnectionString = appInsightsConnectionString));
            Console.WriteLine($"✅ Tracing: Azure Monitor configured for {serviceName}");
        }
        else
        {
            Console.WriteLine("⚠️  Azure exporter selected but APPLICATIONINSIGHTS_CONNECTION_STRING not set");
        }
        break;

    case "none":
    default:
        Console.WriteLine($"ℹ️  Tracing disabled (OTEL_TRACES_EXPORTER={tracesExporter})");
        break;
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Payment Service API v1");
        c.RoutePrefix = string.Empty; // Make Swagger UI the root page
    });
}

// Middleware pipeline
app.UseTraceContext();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// Health checks mapped through operational controller
// app.MapHealthChecks("/health"); // Replaced with operational controller

// Enable Dapr CloudEvents for publishing and subscribing
app.UseCloudEvents();

// Controllers
app.MapControllers();

// Database migration and seeding
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        // Apply pending migrations (creates database if not exists)
        await context.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations");
        throw;
    }
}

app.Logger.LogInformation("Payment Service started successfully");

app.Run();
