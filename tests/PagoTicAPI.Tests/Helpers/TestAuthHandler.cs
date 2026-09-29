using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PagoTicAPI.Tests.Helpers;

/// <summary>
/// Handler de autenticación para tests de integración.
///
/// Solo autentica si el request tiene el header "X-Test-Auth: true".
/// Sin ese header devuelve NoResult → la request se trata como anónima → 401 en endpoints protegidos.
///
/// Uso:
///   - CreateAuthenticatedClient() agrega el header automáticamente.
///   - CreateClient() sin el header → 401 en endpoints que requieren auth.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName  = "TestScheme";
    public const string AuthHeader  = "X-Test-Auth";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Si no tiene el header de test → no autenticar (request anónima)
        if (!Request.Headers.ContainsKey(AuthHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim(ClaimTypes.Name, "test-user"));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "1"));

        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
