using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.TaskHub.Api.Auth;
using Acme.TaskHub.Application;
using Acme.TaskHub.Domain.Exceptions;
using Acme.TaskHub.Infrastructure;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ----- Logging (Serilog) -----
builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration);
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Api.Read", policy => policy.RequireClaim("scp", "api.read"));
    options.AddPolicy("Api.Write", policy => policy.RequireClaim("scp", "api.write"));
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
var otelBuilder = builder.Services.AddOpenTelemetry()
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
