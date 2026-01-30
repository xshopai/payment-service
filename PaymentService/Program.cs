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
        options.JsonSerializerOptions.PropertyNamingPolicy = null; // Keep original property names
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

// Database - use lazy configuration with DaprSecretService
builder.Services.AddDbContext<PaymentDbContext>((serviceProvider, options) =>
{
    var secretService = serviceProvider.GetRequiredService<DaprSecretService>();
    var logger = serviceProvider.GetRequiredService<ILogger<PaymentDbContext>>();
    
    var connectionString = secretService.GetDatabaseConnectionStringAsync().GetAwaiter().GetResult();
    
    if (string.IsNullOrEmpty(connectionString))
    {
        logger.LogError("Database connection string not found in Dapr secrets");
        throw new InvalidOperationException("Database connection string is required");
    }
    
    options.UseSqlServer(
        connectionString,
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null));
});

// JWT Authentication - use lazy configuration with DaprSecretService and caching
builder.Services.AddSingleton<TokenValidationParameters>(serviceProvider =>
{
    // This will be resolved lazily when first needed
    return null!; // Placeholder, will be set up properly in middleware
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Check if TokenValidationParameters is already configured
                if (options.TokenValidationParameters?.IssuerSigningKey == null)
                {
                    // Lazy load JWT configuration from Dapr on first request
                    var secretService = context.HttpContext.RequestServices.GetRequiredService<DaprSecretService>();
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    
                    try
                    {
                        var (jwtKey, jwtIssuer, jwtAudience) = secretService.GetJwtConfigAsync().GetAwaiter().GetResult();
                        
                        if (string.IsNullOrEmpty(jwtKey))
                        {
                            logger.LogError("JWT Key not found in Dapr secrets");
                            throw new InvalidOperationException("JWT Key not found in Dapr secrets");
                        }
                        
                        var key = Encoding.ASCII.GetBytes(jwtKey);
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
                        
                        logger.LogInformation("JWT configuration loaded from Dapr secrets");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to load JWT configuration from Dapr");
                        throw;
                    }
                }
                
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
builder.Services.AddSingleton<DaprEventPublisher>();  // Keep for backward compatibility
builder.Services.AddSingleton<PaymentService.Services.DaprSecretService>();

// Register Messaging abstraction layer (supports dapr, rabbitmq, servicebus via MESSAGING_PROVIDER config)
builder.Services.AddMessaging(builder.Configuration);

// Payment Providers
builder.Services.AddScoped<StripeProvider>();
builder.Services.AddScoped<PayPalProvider>();
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
// NOTE: Commented out due to SQL Server connection issues
// Uncomment when SQL Server is properly configured
/*
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        // Apply pending migrations
        await context.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations");
        throw;
    }
}
*/

app.Logger.LogInformation("Payment Service started successfully");

app.Run();
