namespace PagoTicAPI.Application.Configuration.AutomaticDebits;

public sealed class PayPerTicJurisdictionTokenProvider : IPayPerTicJurisdictionTokenProvider
{
    private static readonly TimeSpan RefreshSafetyMargin = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinimumTokenLifetime = TimeSpan.FromSeconds(1);
    private static readonly ConditionalWeakTable<IMemoryCache, ConcurrentDictionary<long, SemaphoreSlim>>
        RefreshLocksByCache = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _refreshLocks;
    private readonly IJurisdictionPayPerTicConfigurationProvider _configurationProvider;
    private readonly IPayPerTicTokenAcquirer _tokenAcquirer;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PayPerTicJurisdictionTokenProvider> _logger;

    public PayPerTicJurisdictionTokenProvider(
        IJurisdictionPayPerTicConfigurationProvider configurationProvider,
        IPayPerTicTokenAcquirer tokenAcquirer,
        IMemoryCache cache,
        TimeProvider timeProvider,
        ILogger<PayPerTicJurisdictionTokenProvider> logger)
    {
        _configurationProvider = configurationProvider;
        _tokenAcquirer = tokenAcquirer;
        _cache = cache;
        _refreshLocks = RefreshLocksByCache.GetValue(
            cache,
            static _ => new ConcurrentDictionary<long, SemaphoreSlim>());
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(
        long jurisdictionId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
        var cacheKey = AutomaticDebitCacheKeys.PayPerTicToken(
            jurisdictionId,
            Fingerprint(configuration));
        if (TryGetValidToken(cacheKey, out var cachedToken))
            return cachedToken;

        var refreshLock = _refreshLocks.GetOrAdd(jurisdictionId, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
            cacheKey = AutomaticDebitCacheKeys.PayPerTicToken(
                jurisdictionId,
                Fingerprint(configuration));
            if (TryGetValidToken(cacheKey, out cachedToken))
                return cachedToken;

            var acquired = await _tokenAcquirer.AcquireAsync(configuration, cancellationToken);
            if (string.IsNullOrWhiteSpace(acquired.AccessToken))
                throw new InvalidOperationException("PayPerTIC returned an empty access token.");

            var lifetime = TimeSpan.FromSeconds(Math.Max(acquired.ExpiresInSeconds, 1));
            var refreshAfter = lifetime > RefreshSafetyMargin
                ? lifetime - RefreshSafetyMargin
                : MinimumTokenLifetime;
            var entry = new CachedToken(acquired.AccessToken, _timeProvider.GetUtcNow().Add(refreshAfter));
            _cache.Set(cacheKey, entry, lifetime);

            _logger.LogInformation(
                "Se almacenó en caché el token de débito automático PayPerTIC para la jurisdicción {JurisdictionId}; vigencia {LifetimeSeconds}s.",
                jurisdictionId,
                lifetime.TotalSeconds);
            return entry.Value;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private bool TryGetValidToken(string cacheKey, out string token)
    {
        if (_cache.TryGetValue(cacheKey, out CachedToken? entry) &&
            entry is not null &&
            entry.RefreshAfter > _timeProvider.GetUtcNow())
        {
            token = entry.Value;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private static string Fingerprint(JurisdictionPayPerTicConfiguration configuration)
    {
        var material = string.Join(
            '\u001f',
            configuration.AuthUrl,
            configuration.ApiUrl,
            configuration.Username,
            configuration.Password,
            configuration.ClientId,
            configuration.ClientSecret,
            configuration.CollectorId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
    }

    private sealed record CachedToken(string Value, DateTimeOffset RefreshAfter);
}
