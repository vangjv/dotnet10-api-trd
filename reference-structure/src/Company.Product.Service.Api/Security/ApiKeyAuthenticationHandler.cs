using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Company.Product.Service.Api.Security;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string[] Keys { get; set; } = [];

    internal static bool IsValidKey(string? key) => key is { Length: 64 }
        && key.All(Uri.IsHexDigit);
}

public static class ApiKeyAuthenticationDefaults
{
    public const string AuthenticationScheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (headerValues.Count != 1 || !ApiKeyAuthenticationOptions.IsValidKey(headerValues[0]))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var providedKey = Encoding.ASCII.GetBytes(headerValues[0]!);
        var matched = false;
        foreach (var key in Options.Keys)
            matched |= CryptographicOperations.FixedTimeEquals(providedKey, Encoding.ASCII.GetBytes(key));
        if (!matched)
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
