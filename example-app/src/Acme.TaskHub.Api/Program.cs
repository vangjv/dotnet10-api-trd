using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Acme.TaskHub.Api.Auth;
using Acme.TaskHub.Application;
using Acme.TaskHub.Application.Instrumentation;
using Acme.TaskHub.Domain.Exceptions;
using Acme.TaskHub.Infrastructure;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Web;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ----- Logging (Serilog) -----
// TRD §9.1: Enrich every log event with static application identity so logs from different
// services are distinguishable in Azure Monitor / Log Analytics. WithProperty must be
// configured here in code — it cannot be specified in appsettings.json enricher arrays.
builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.WithProperty("Application", "Acme.TaskHub.Api")
       .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName);
});

// ----- Controllers + API Behavior -----
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Customize ProblemDetails for model binding errors.
        options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(context.ModelState)
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest,
            };
            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            return new BadRequestObjectResult(problem);
        };
    });

// ----- Rate Limiting (TRD §8.5) -----
// Fixed-window policy for write operations — 60 requests / user / minute.
// Built into ASP.NET Core 7+ (Microsoft.AspNetCore.RateLimiting); no extra NuGet needed.
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("ApiWrite", o =>
    {
        o.PermitLimit = 60;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0; // reject immediately — do not queue
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ----- CORS (TRD §8.7) -----
// Deny-all by default; configure Cors:AllowedOrigins in environment settings.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowedOrigins", policy =>
        policy
            .WithOrigins(
                builder.Configuration.GetSection("Cors:AllowedOrigins")
                       .Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Acme TaskHub API",
        Version = "v1",
        Description = "Enterprise-grade Task Management API following the .NET 10 TRD.",
    });
});

// ----- API Versioning (TRD §6.2: URL versioning) -----
builder.Services.AddApiVersioning(o =>
{
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
    o.ReportApiVersions = true;
}).AddMvc();

// ----- Auth (Entra ID / Development bypass) -----
// Policies are registered in ALL environments so [Authorize(Policy = "...")]
// attributes work the same way in dev and prod. In Development a synthetic
// DevAuthHandler auto-succeeds and issues the required scope claims.
if (builder.Environment.IsDevelopment())
{
    builder.Services
        .AddAuthentication("DevScheme")
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAuthHandler>(
            "DevScheme", _ => { });
}
else
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
}

// TRD §8.1: Use RequireAssertion to handle both individual "scp" claims and
// space-delimited single-claim format that Entra ID may emit.
// RequireClaim("scp", "api.read") performs an exact-value match and silently
// fails when scopes arrive as a single space-separated string.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Api.Read", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.FindAll("scp").Any(c =>
                c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                 .Contains("api.read", StringComparer.OrdinalIgnoreCase))));

    options.AddPolicy("Api.Write", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.FindAll("scp").Any(c =>
                c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                 .Contains("api.write", StringComparer.OrdinalIgnoreCase))));
});

// ----- Validation (manual — see controllers) -----
// Validators are registered in AddApplication() within the Application assembly.
// Do NOT call AddFluentValidationAutoValidation() — it conflicts with manual validation
// and produces inconsistent ValidationProblemDetails (TRD §4.4).

// ----- Application + Infrastructure services -----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Health checks are registered inside AddInfrastructure (TRD §5.3, §5.6).

// ----- Resilience (Polly v8 via Microsoft.Extensions.Http.Resilience) -----
// Registers a named HttpClient with a standard resilience pipeline per TRD §10:
//   - Total request timeout: 30s
//   - Retry: 3 attempts, exponential backoff with jitter for transient/5xx/429
//   - Circuit breaker: opens after sustained failures
//   - Per-attempt timeout: 10s (TRD §10.1 default)
builder.Services.AddHttpClient("Downstream")
    .AddStandardResilienceHandler(options =>
    {
        // TRD §10.1: Default per-attempt timeout 10s.
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);

        // TRD §10.2: Max 3 retries with exponential backoff + jitter.
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;

        // TRD §10.1: Total timeout 30s across all attempts.
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });

// ----- OpenTelemetry -----
// TRD §9.3: ConfigureResource sets service.name, service.version, and deployment.environment
// so traces and metrics in Azure Monitor are identifiable per service/deployment.
// Without this, telemetry appears as an unnamed service in all environments.
var otelBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "Acme.TaskHub.Api",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
        }))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddEntityFrameworkCoreInstrumentation();

        // Use console exporter in Development; Azure Monitor in Production.
        if (builder.Environment.IsDevelopment())
        {
            tracing.AddConsoleExporter();
        }
        else
        {
            tracing.AddAzureMonitorTraceExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();

        // TRD §9.2: Register the application business-metrics meter so the OTel SDK
        // collects and exports it. The meter is populated by TodoItemMetrics.
        metrics.AddMeter(TodoItemMetrics.MeterName);

        if (!builder.Environment.IsDevelopment())
        {
            metrics.AddAzureMonitorMetricExporter();
        }
    });

var app = builder.Build();

// ----- Ensure database is created (Development only — TRD §11.2) -----
// Encapsulated in Infrastructure to avoid referencing AppDbContext in Program.cs (TRD §5.3).
await app.InitializeDatabaseAsync();

// ----- Pipeline -----

// Security headers (TRD §8.6) — applied before all other middleware so every
// response (including error responses) carries these headers.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Cache-Control", "no-store");
    await next(context);
});

// Correlation ID enrichment (TRD §9.1) — reads or generates an X-Correlation-ID header,
// echoes it back to the caller, and pushes it into the Serilog log context so every
// log event in the request scope is tagged with the same correlation ID.
app.Use(async (context, next) =>
{
    const string headerName = "X-Correlation-ID";
    if (!context.Request.Headers.TryGetValue(headerName, out var correlationId)
        || string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString();
    }

    context.Response.Headers.Append(headerName, correlationId.ToString());

    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId.ToString()))
    {
        await next(context);
    }
});

app.UseSerilogRequestLogging();

// Centralized exception handler — translates domain exceptions to RFC 7807 ProblemDetails.
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

        ProblemDetails problem;

        if (exception is DomainValidationException domainValidationEx)
        {
            problem = new ProblemDetails
            {
                Title = "A domain validation rule was violated.",
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = domainValidationEx.Message,
            };
            problem.Extensions["errorCode"] = domainValidationEx.ErrorCode;
        }
        else if (exception is DomainException domainEx)
        {
            problem = new ProblemDetails
            {
                Title = "A domain rule was violated.",
                Status = StatusCodes.Status409Conflict,
                Detail = domainEx.Message,
            };
            problem.Extensions["errorCode"] = domainEx.ErrorCode;
        }
        else
        {
            problem = new ProblemDetails
            {
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "See logs with the provided traceId.",
            };
        }

        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        context.Response.StatusCode = problem.Status!.Value;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem).ConfigureAwait(false);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Acme TaskHub API v1");
    });
}

app.UseHttpsRedirection();

app.UseCors("AllowedOrigins");  // TRD §8.7 — before auth so preflight OPTIONS succeeds
app.UseRateLimiter();            // TRD §8.5

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health endpoints per TRD §3.1 and §14.1.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false, // Liveness: process health only — no dependency checks.
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "database",
});

app.Run();

/// <summary>
/// Marker type for the API assembly (used by WebApplicationFactory and integration tests).
/// </summary>
public partial class Program
{
}
