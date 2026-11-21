using System.Diagnostics;
using System.Text.Json.Serialization;
using Company.Product.Service.Api.Features.Customers;
using Company.Product.Service.Api.Observability;
using Company.Product.Service.Api.Security;
using Company.Product.Service.Application;
using Company.Product.Service.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Host.UseSerilog((context, log) => log
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CanonicalLogFormatter(context.Configuration)));

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity)
        context.ProblemDetails.Extensions["traceId"] = activity.TraceId.ToHexString();
    else
        context.ProblemDetails.Extensions["requestId"] = context.HttpContext.TraceIdentifier;

    context.ProblemDetails.Extensions.TryAdd("errorCode", context.ProblemDetails.Status switch
    {
        400 => "invalid_request",
        401 => "authentication_required",
        403 => "forbidden",
        404 => "not_found",
        413 => "payload_too_large",
        500 => "internal_error",
        var status => $"http_{status}",
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "Company.Product.Service.Api.xml"));
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ApiKeyAuthenticationDefaults.HeaderName,
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("ApiKey", document)] = [],
    });
});
builder.Services.AddScoped<FluentValidation.IValidator<RenameCustomerRequest>, RenameCustomerValidator>();

const string apiKeyScheme = ApiKeyAuthenticationDefaults.AuthenticationScheme;
builder.Services.AddAuthentication(apiKeyScheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(apiKeyScheme, _ => { });
builder.Services.AddOptions<ApiKeyAuthenticationOptions>(apiKeyScheme)
    .BindConfiguration("ApiKey")
    .Validate(options => options.Keys.Length > 0
        && options.Keys.Distinct(StringComparer.Ordinal).Count() == options.Keys.Length
        && options.Keys.All(ApiKeyAuthenticationOptions.IsValidKey),
        "Configure at least one distinct 256-bit API key (64 hexadecimal characters).")
    .ValidateOnStart();
builder.Services.AddAuthorization(options =>
{
    var policy = new AuthorizationPolicyBuilder(apiKeyScheme).RequireAuthenticatedUser().Build();
    options.DefaultPolicy = policy;
    options.FallbackPolicy = policy;
    options.AddPolicy("RequireApiKey", policy);
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure();

var app = builder.Build();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "Completed HTTP request";
    options.EnrichDiagnosticContext = (diagnostic, http) =>
    {
        diagnostic.Set("EventName", "request.completed");
        diagnostic.Set("RequestId", http.TraceIdentifier);
        diagnostic.Set("RequestMethod", http.Request.Method);
        if (http.GetEndpoint() is RouteEndpoint endpoint && endpoint.RoutePattern.RawText is { } route)
            diagnostic.Set("RouteTemplate", route);
        if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity)
        {
            diagnostic.Set("TraceId", activity.TraceId.ToHexString());
            diagnostic.Set("SpanId", activity.SpanId.ToHexString());
        }
    };
    options.GetLevel = (http, _, exception) => exception is not null || http.Response.StatusCode >= 500
        ? LogEventLevel.Error
        : http.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Debug : LogEventLevel.Information;
});
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.MapSwagger().AllowAnonymous();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapCustomers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();
app.Run();

public partial class Program;
