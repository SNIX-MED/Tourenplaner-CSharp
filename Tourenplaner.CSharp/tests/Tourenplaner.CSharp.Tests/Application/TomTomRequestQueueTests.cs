using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class TomTomRequestQueueTests
{
    [Fact]
    public async Task RateLimit_RetriesAfterServerDelay_AndReturnsSuccess()
    {
        var clock = new TestClock();
        var calls = 0;
        using var client = new HttpClient(new Handler(() => ++calls == 1
            ? Limited(TimeSpan.FromSeconds(3)) : new(HttpStatusCode.OK)));
        var progress = new List<string>();
        using var response = await clock.Queue.GetAsync(client, "https://example.test", progress.Add);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
        Assert.Contains(TimeSpan.FromSeconds(3), clock.Delays);
        Assert.Single(progress);
    }

    [Fact]
    public async Task PermanentRateLimit_StopsAfterFiveAttempts()
    {
        var clock = new TestClock();
        var calls = 0;
        using var client = new HttpClient(new Handler(() => { calls++; return Limited(); }));
        using var response = await clock.Queue.GetAsync(client, "https://example.test");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(5, calls);
        Assert.Equal(new[] { 2.0, 4.0, 8.0, 16.0 }, clock.Delays.Select(x => x.TotalSeconds));
    }

    [Fact]
    public async Task LongServerCooldown_IsNotIgnoredByNextCaller()
    {
        var clock = new TestClock();
        var calls = 0;
        using var client = new HttpClient(new Handler(() => { calls++; return Limited(TimeSpan.FromMinutes(5)); }));
        using var first = await clock.Queue.GetAsync(client, "https://example.test/first");
        using var second = await clock.Queue.GetAsync(client, "https://example.test/second");
        Assert.Equal(1, calls);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task MultipleCallers_KeepMinimumSpacing()
    {
        var clock = new TestClock();
        var times = new List<DateTimeOffset>();
        using var client = new HttpClient(new Handler(() => { times.Add(clock.Now); return new(HttpStatusCode.OK); }));
        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => clock.Queue.GetAsync(client, "https://example.test")));
        foreach (var response in responses) response.Dispose();
        Assert.Equal(6, times.Count);
        Assert.All(times.Zip(times.Skip(1)), pair => Assert.True(pair.Second - pair.First >= TimeSpan.FromMilliseconds(600)));
    }

    [Fact]
    public async Task AuthenticationFailure_IsNotRetried()
    {
        var clock = new TestClock();
        var calls = 0;
        using var client = new HttpClient(new Handler(() => { calls++; return new(HttpStatusCode.Forbidden); }));
        using var response = await clock.Queue.GetAsync(client, "https://example.test");
        Assert.Equal(1, calls);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task RetryAfterDate_IsRespected()
    {
        var clock = new TestClock();
        var calls = 0;
        using var client = new HttpClient(new Handler(() =>
        {
            if (++calls > 1) return new(HttpStatusCode.OK);
            var limited = Limited();
            limited.Headers.RetryAfter = new RetryConditionHeaderValue(clock.Now.AddSeconds(7));
            return limited;
        }));
        using var response = await clock.Queue.GetAsync(client, "https://example.test");
        Assert.Contains(TimeSpan.FromSeconds(7), clock.Delays);
    }

    private static HttpResponseMessage Limited(TimeSpan? delay = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (delay.HasValue) response.Headers.RetryAfter = new RetryConditionHeaderValue(delay.Value);
        return response;
    }

    private sealed class TestClock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Delays { get; } = [];
        public TomTomRequestQueue Queue { get; }
        public TestClock() => Queue = new(() => Now, async (wait, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(wait);
            Now += wait;
            await Task.Yield();
        });
    }

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond());
    }
}
