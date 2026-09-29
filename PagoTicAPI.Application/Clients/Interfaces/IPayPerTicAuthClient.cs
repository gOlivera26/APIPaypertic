namespace PagoTicAPI.Application.Clients.Interfaces;

/// <summary>
/// Obtiene y cachea el Bearer token de PayPerTIC (OAuth2).
/// El token se renueva automáticamente antes de que expire.
/// </summary>
public interface IPayPerTicAuthClient
{
    /// <summary>
    /// Retorna un Bearer token válido.
    /// Si el token cacheado aún es válido lo reutiliza;
    /// de lo contrario solicita uno nuevo al endpoint de autenticación de PayPerTIC.
    /// </summary>
    Task<string> GetTokenAsync();
}
