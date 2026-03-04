using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.TaskHub.Tests.Integration;

/// <summary>
/// <see cref="CustomWebApplicationFactory"/> variant that replaces the <c>DevAuthHandler</c>
/// with a handler that always fails authentication (TRD §13.3).
/// Use this factory in tests that verify unauthenticated requests receive <c>401 Unauthorized</c>
/// and that authorization policies are enforced when no credentials are present.
/// </summary>
public sealed class UnauthenticatedWebApplicationFactory : CustomWebApplicationFactory
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder); // sets Development environment + in-memory SQLite

        // ConfigureTestServices runs after the application's ConfigureServices, so it
        // can override the DevAuthHandler that was registered by Program.cs.
        builder.ConfigureTestServices(services =>
        {
            // Replace the default scheme with one that always returns Fail so that
            // [Authorize] endpoints challenge back with 401.
            services
                .AddAuthentication("RejectAll")
                .AddScheme<AuthenticationSchemeOptions, RejectAllAuthHandler>(
                    "RejectAll",
                    _ => { });
        });
    }

    /// <summary>
    /// Authentication handler that always fails.
    /// Returns <see cref="AuthenticateResult.Fail"/> so every request is treated as
    /// unauthenticated and <c>[Authorize]</c> policies challenge with 401.
    /// </summary>
    private sealed class RejectAllAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RejectAllAuthHandler"/> class.
        /// </summary>
        public RejectAllAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <inheritdoc />
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.Fail("No credentials provided."));
    }
}
