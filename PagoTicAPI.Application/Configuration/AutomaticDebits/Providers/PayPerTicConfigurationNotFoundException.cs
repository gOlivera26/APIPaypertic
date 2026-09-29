namespace PagoTicAPI.Application.Configuration.AutomaticDebits;

public sealed class PayPerTicConfigurationNotFoundException(long jurisdictionId)
    : InvalidOperationException($"No existe una configuración activa de PayPerTIC para la jurisdicción {jurisdictionId}.");
