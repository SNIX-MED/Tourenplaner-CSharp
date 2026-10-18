using System.Net.Http;

namespace Tourenplaner.CSharp.Shared.Net;

internal static class ConfiguredHttpClientFactory
{
    public static HttpClient Create(
        TimeSpan timeout,
        string? userAgent = null,
        bool acceptJson = false)
    {
        var client = new HttpClient
        {
            Timeout = timeout
        };

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }

        if (acceptJson)
        {
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        return client;
    }
}
