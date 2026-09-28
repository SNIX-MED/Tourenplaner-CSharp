using System.Net;
using System.Net.Http;

namespace Tourenplaner.CSharp.App.Services;

/// <summary>Shared by all geocoding callers in this process, including import and map views.</summary>
public sealed class TomTomRequestQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private DateTimeOffset _nextRequest;

    public TomTomRequestQueue() : this(() => DateTimeOffset.UtcNow, Task.Delay) { }

    public TomTomRequestQueue(Func<DateTimeOffset> now, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _now = now;
        _delay = delay;
    }

    public async Task<HttpResponseMessage> GetAsync(HttpClient client, string uri,
        Action<string>? reportProgress = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Below the default 5 requests/second, leaving room for another workstation.
            for (var attempt = 0; ; attempt++)
            {
                var remaining = _nextRequest - _now();
                if (remaining > TimeSpan.FromSeconds(30))
                {
                    var deferred = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    deferred.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(remaining);
                    return deferred;
                }
                if (remaining > TimeSpan.Zero)
                    await _delay(remaining, cancellationToken);
                _nextRequest = _now() + TimeSpan.FromMilliseconds(600);
                var response = await client.GetAsync(uri, cancellationToken);
                if (response.StatusCode != HttpStatusCode.TooManyRequests)
                    return response;

                var retryAfter = response.Headers.RetryAfter;
                var wait = retryAfter?.Delta ?? (retryAfter?.Date - _now()) ?? TimeSpan.Zero;
                wait = wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _nextRequest = _now() + (wait > TimeSpan.FromMilliseconds(600) ? wait : TimeSpan.FromMilliseconds(600));
                // Keep a persistent restriction finite; never ignore a longer server cooldown.
                if (attempt >= 4 || wait > TimeSpan.FromSeconds(30))
                    return response;
                response.Dispose();
                reportProgress?.Invoke($"TomTom drosselt vorübergehend. Neuer Versuch in {Math.Ceiling(wait.TotalSeconds):0} Sekunden ({attempt + 1}/4)…");
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
