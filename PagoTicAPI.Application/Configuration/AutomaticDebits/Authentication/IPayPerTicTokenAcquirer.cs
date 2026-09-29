namespace PagoTicAPI.Application.Configuration.AutomaticDebits;

/// <summary>Adquiere tokens OAuth utilizando la configuración de una jurisdicción.</summary>
public interface IPayPerTicTokenAcquirer
{
    /// <summary>Solicita a PayPerTIC un token de acceso para la configuración indicada.</summary>
    Task<PayPerTicAccessToken> AcquireAsync(JurisdictionPayPerTicConfiguration configuration, CancellationToken cancellationToken = default);
}
