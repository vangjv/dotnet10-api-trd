using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Acme.TaskHub.Api.Auth;

/// <summary>
/// A no-op authentication handler for local development.
/// Automatically authenticates all requests so endpoints can be tested without Entra ID tokens.
/// </summary>
/// <remarks>
/// Registered only when <c>IsDevelopment()</c> is true (TRD §8.2.1).
/// Issues individual <c>scp</c> claims so all <c>RequireClaim("scp", "...")</c> policies succeed,
/// exercising the authorization pipeline identically to production.
/// </remarks>
internal sealed class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DevAuthHandler"/> class.
    /// </summary>
    public DevAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity("Development");
        identity.AddClaim(new Claim("sub", "dev-user"));

        // Issue individual scope claims so RequireClaim("scp", "api.read") and
        // RequireClaim("scp", "api.write") policies succeed in development.
        identity.AddClaim(new Claim("scp", "api.read"));
        identity.AddClaim(new Claim("scp", "api.write"));

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "DevScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
