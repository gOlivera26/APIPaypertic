using PagoTicAPI.Domain.Models.AutomaticDebits;

namespace PagoTicAPI.Application.Configuration.AutomaticDebits;

public sealed class JurisdictionPayPerTicConfigurationProvider : IJurisdictionPayPerTicConfigurationProvider
{
    private readonly gtwContext _context;
    private readonly ILogger<JurisdictionPayPerTicConfigurationProvider> _logger;

    public JurisdictionPayPerTicConfigurationProvider(
        gtwContext context,
        ILogger<JurisdictionPayPerTicConfigurationProvider> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<JurisdictionPayPerTicConfiguration> GetActiveAsync(
        long jurisdictionId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _context.PayPerTicConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.JurisdictionId == jurisdictionId && item.IsActive,
                cancellationToken);

        if (configuration is null)
        {
            _logger.LogWarning(
                "No existe una configuración activa de débito automático PayPerTIC para la jurisdicción {JurisdictionId}.",
                jurisdictionId);
            throw new PayPerTicConfigurationNotFoundException(jurisdictionId);
        }

        _logger.LogDebug(
            "Se resolvió la configuración de débito automático PayPerTIC para la jurisdicción {JurisdictionId}.",
            jurisdictionId);
        return Map(configuration);
    }

    private static JurisdictionPayPerTicConfiguration Map(PayPerTicConfiguration configuration) => new(
        configuration.JurisdictionId,
        configuration.AuthUrl,
        configuration.ApiUrl,
        configuration.Username,
        configuration.Password,
        configuration.ClientId,
        configuration.ClientSecret,
        configuration.CollectorId,
        configuration.NotificationUrl,
        configuration.ReturnUrl,
        configuration.BackUrl);
}

