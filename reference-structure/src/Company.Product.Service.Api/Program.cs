using System.Diagnostics;
using Company.Product.Service.Api.Security;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    .ConfigureApiBehaviorOptions(options =>
    {
        // Keep default ProblemDetails behavior; customize in middleware if desired.
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
builder.Services.AddSwaggerGen();

// ----- API Versioning -----
builder.Services.AddApiVersioning(o =>
{
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.ReportApiVersions = true;
}).AddMvc();

// ----- Auth (interim API Key scheme) -----
builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.AuthenticationScheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.AuthenticationScheme,
        options => builder.Configuration.GetSection("ApiKey").Bind(options));

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(ApiKeyAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("RequireApiKey", options.DefaultPolicy);
});

// ----- Validation -----
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ----- App services -----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ----- Health checks -----
builder.Services.AddHealthChecks()
    .AddCheck<ReadyHealthCheck>("ready");

// Datadog APM is provided by dd-trace-dotnet auto-instrumentation (Datadog.Trace.Bundle) and the Agent sidecar; no code-level setup needed.

var app = builder.Build();

// ----- Pipeline -----
app.UseSerilogRequestLogging();

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        // Centralized minimal ProblemDetails (customize per org)
        var problem = new ProblemDetails
        {
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError,
            Detail = "See logs with the provided traceId.",
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        context.Response.StatusCode = problem.Status.Value;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.Run();

public sealed class ReadyHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // TODO: check critical dependencies (DB connectivity, etc.)
        return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("ready"));
    }
}
