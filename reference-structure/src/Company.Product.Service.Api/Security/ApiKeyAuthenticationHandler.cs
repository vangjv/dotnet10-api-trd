using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Company.Product.Service.Api.Security;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-Api-Key";

    public IReadOnlyCollection<string> Keys { get; set; } = [];
}

public static class ApiKeyAuthenticationDefaults
{
    public const string AuthenticationScheme = "ApiKey";
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedKey = headerValues.ToString();
        if (!Options.Keys.Contains(providedKey, StringComparer.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "api-key-client")],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
