using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.TaskHub.Application;
using Acme.TaskHub.Domain.Exceptions;
using Acme.TaskHub.Infrastructure;
using Acme.TaskHub.Infrastructure.Persistence;
using Azure.Monitor.OpenTelemetry.Exporter;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

// ----- JSON serialization (System.Text.Json with TRD conventions) -----
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
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
if (builder.Environment.IsDevelopment())
{
    // In Development, use a no-op authentication scheme so we can test without Entra ID.
    builder.Services
        .AddAuthentication("DevScheme")
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAuthHandler>(
            "DevScheme", _ => { });

    builder.Services.AddAuthorization();
}
else
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Api.Read", policy => policy.RequireClaim("scp", "api.read"));
        options.AddPolicy("Api.Write", policy => policy.RequireClaim("scp", "api.write"));
    });
}

// ----- Validation -----
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ----- Application + Infrastructure services -----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ----- Health checks -----
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

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

// ----- Ensure database is created (Development only) -----
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
}

// ----- Pipeline -----
app.UseSerilogRequestLogging();

// Centralized exception handler — translates domain exceptions to RFC 7807 ProblemDetails.
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

        ProblemDetails problem;

        if (exception is DomainException domainEx)
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

// ----- Development auth handler (auto-succeeds; non-production only) -----

/// <summary>
/// A no-op authentication handler for local development.
/// Automatically authenticates all requests so endpoints can be tested without Entra ID.
/// </summary>
internal sealed class DevAuthHandler
    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<
        Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DevAuthHandler"/> class.
    /// </summary>
    public DevAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<
            Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new System.Security.Claims.ClaimsIdentity("Development");
        identity.AddClaim(new System.Security.Claims.Claim("sub", "dev-user"));
        identity.AddClaim(new System.Security.Claims.Claim("scp", "api.read api.write"));

        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, "DevScheme");

        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Marker type for the API assembly (used by WebApplicationFactory and integration tests).
/// </summary>
public partial class Program
{
}
