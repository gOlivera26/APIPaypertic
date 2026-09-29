using System.Collections.Concurrent;
using PagoTicAPI.Application.Configuration.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public class TokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_ForTwoJurisdictions_CachesTokensIndependently()
    {
        var configurations = new Mock<IJurisdictionPayPerTicConfigurationProvider>();
        configurations.Setup(x => x.GetActiveAsync(2419, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConfiguration(2419));
        configurations.Setup(x => x.GetActiveAsync(2389, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConfiguration(2389));
        var acquirer = new RecordingTokenAcquirer();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new PayPerTicJurisdictionTokenProvider(
            configurations.Object,
            acquirer,
            cache,
            TimeProvider.System,
            Mock.Of<ILogger<PayPerTicJurisdictionTokenProvider>>());

        var vgbFirst = await sut.GetTokenAsync(2419);
        var unqFirst = await sut.GetTokenAsync(2389);
        var vgbSecond = await sut.GetTokenAsync(2419);
        var unqSecond = await sut.GetTokenAsync(2389);

        vgbFirst.Should().Be("token-2419-1");
        unqFirst.Should().Be("token-2389-1");
        vgbSecond.Should().Be(vgbFirst);
        unqSecond.Should().Be(unqFirst);
        acquirer.Calls.Should().BeEquivalentTo(new[] { 2419L, 2389L });
    }

    [Fact]
    public async Task GetTokenAsync_WhenOnlyOneTokenExpires_RefreshesOnlyThatJurisdiction()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));
        var configurations = new Mock<IJurisdictionPayPerTicConfigurationProvider>();
        configurations.Setup(x => x.GetActiveAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long jurisdictionId, CancellationToken _) => CreateConfiguration(jurisdictionId));
        var acquirer = new RecordingTokenAcquirer(jurisdictionId => jurisdictionId == 2419 ? 60 : 600);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new PayPerTicJurisdictionTokenProvider(
            configurations.Object,
            acquirer,
            cache,
            clock,
            Mock.Of<ILogger<PayPerTicJurisdictionTokenProvider>>());

        var vgbFirst = await sut.GetTokenAsync(2419);
        var unqFirst = await sut.GetTokenAsync(2389);
        clock.Advance(TimeSpan.FromSeconds(31));
        var vgbRefreshed = await sut.GetTokenAsync(2419);
        var unqCached = await sut.GetTokenAsync(2389);

        vgbRefreshed.Should().NotBe(vgbFirst);
        unqCached.Should().Be(unqFirst);
        acquirer.Calls.Count(x => x == 2419).Should().Be(2);
        acquirer.Calls.Count(x => x == 2389).Should().Be(1);
    }

    [Fact]
    public async Task GetTokenAsync_WhenConcurrentRequestsMissCache_AcquiresOneTokenPerJurisdiction()
    {
        var configurations = new Mock<IJurisdictionPayPerTicConfigurationProvider>();
        configurations.Setup(x => x.GetActiveAsync(2419, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConfiguration(2419));
        var acquirer = new RecordingTokenAcquirer(delay: TimeSpan.FromMilliseconds(25));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new PayPerTicJurisdictionTokenProvider(
            configurations.Object,
            acquirer,
            cache,
            TimeProvider.System,
            Mock.Of<ILogger<PayPerTicJurisdictionTokenProvider>>());

        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => sut.GetTokenAsync(2419)));

        tokens.Should().OnlyContain(token => token == "token-2419-1");
        acquirer.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task GetTokenAsync_AcrossTwoProviderInstancesSharingCache_AcquiresOneTokenPerJurisdiction()
    {
        var configurations = new Mock<IJurisdictionPayPerTicConfigurationProvider>();
        configurations.Setup(x => x.GetActiveAsync(2419, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConfiguration(2419));
        var acquirer = new RecordingTokenAcquirer(delay: TimeSpan.FromMilliseconds(100));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var firstProvider = CreateProvider(configurations.Object, acquirer, cache);
        var secondProvider = CreateProvider(configurations.Object, acquirer, cache);
        using var callersReady = new CountdownEvent(2);
        using var startRequests = new ManualResetEventSlim();

        var firstRequest = StartConcurrentRequest(firstProvider, callersReady, startRequests);
        var secondRequest = StartConcurrentRequest(secondProvider, callersReady, startRequests);
        callersReady.Wait();
        startRequests.Set();
        var tokens = await Task.WhenAll(firstRequest, secondRequest);

        tokens.Should().OnlyContain(token => token == "token-2419-1");
        acquirer.Calls.Should().ContainSingle().Which.Should().Be(2419);
    }

    [Fact]
    public async Task GetTokenAsync_WhenCredentialsRotate_DoesNotReusePreviousToken()
    {
        var current = CreateConfiguration(2419);
        var configurations = new Mock<IJurisdictionPayPerTicConfigurationProvider>();
        configurations.Setup(x => x.GetActiveAsync(2419, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => current);
        var acquirer = new RecordingTokenAcquirer();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateProvider(configurations.Object, acquirer, cache);

        var first = await sut.GetTokenAsync(2419);
        current = current with { ClientSecret = "rotated-secret" };
        var afterRotation = await sut.GetTokenAsync(2419);

        afterRotation.Should().NotBe(first);
        acquirer.Calls.Count(x => x == 2419).Should().Be(2);
    }

    private static PayPerTicJurisdictionTokenProvider CreateProvider(
        IJurisdictionPayPerTicConfigurationProvider configurations,
        IPayPerTicTokenAcquirer acquirer,
        IMemoryCache cache) => new(
            configurations,
            acquirer,
            cache,
            TimeProvider.System,
            Mock.Of<ILogger<PayPerTicJurisdictionTokenProvider>>());

    private static Task<string> StartConcurrentRequest(
        PayPerTicJurisdictionTokenProvider provider,
        CountdownEvent callersReady,
        ManualResetEventSlim startRequests) => Task.Run(async () =>
    {
        callersReady.Signal();
        startRequests.Wait();
        return await provider.GetTokenAsync(2419);
    });

    private static JurisdictionPayPerTicConfiguration CreateConfiguration(long jurisdictionId) => new(
        jurisdictionId,
        $"https://auth-{jurisdictionId}.example.test/token",
        $"https://api-{jurisdictionId}.example.test",
        $"user-{jurisdictionId}",
        $"password-{jurisdictionId}",
        $"client-{jurisdictionId}",
        $"secret-{jurisdictionId}",
        $"collector-{jurisdictionId}",
        null,
        null,
        null);

    private sealed class RecordingTokenAcquirer : IPayPerTicTokenAcquirer
    {
        private readonly Func<long, int> _expiresInSeconds;
        private readonly TimeSpan _delay;
        private readonly ConcurrentDictionary<long, int> _counts = new();
        private readonly ConcurrentQueue<long> _calls = new();

        public RecordingTokenAcquirer(
            Func<long, int>? expiresInSeconds = null,
            TimeSpan delay = default)
        {
            _expiresInSeconds = expiresInSeconds ?? (_ => 600);
            _delay = delay;
        }

        public IReadOnlyCollection<long> Calls => _calls.ToArray();

        public async Task<PayPerTicAccessToken> AcquireAsync(
            JurisdictionPayPerTicConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            _calls.Enqueue(configuration.JurisdictionId);
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken);

            var count = _counts.AddOrUpdate(configuration.JurisdictionId, 1, (_, current) => current + 1);
            return new PayPerTicAccessToken(
                $"token-{configuration.JurisdictionId}-{count}",
                _expiresInSeconds(configuration.JurisdictionId));
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public TestTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed) => _utcNow = _utcNow.Add(elapsed);
    }
}
