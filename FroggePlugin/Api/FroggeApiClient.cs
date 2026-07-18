using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace FroggePlugin.Api;

public sealed class FroggeApiClient : IDisposable
{
    private readonly HttpClient httpClient;

    public FroggeApiClient(Configuration configuration)
    {
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(configuration.ApiBaseUrl)
        };

        // No auth header is attached yet, deliberately. FroggeAPI's existing service-token
        // scheme (Schemas/src/schemas/servicetoken.py) signs with an HMAC secret shared only
        // between the API/Bot/Worker server processes. It must never ship inside this plugin's
        // DLL, since any player could decompile it and mint tokens for any guild. A separate,
        // narrowly-scoped per-character token flow needs to be designed on the API side before
        // this client can authenticate against guild-scoped routes.
    }

    public async Task<bool> PingAsync()
    {
        using var response = await httpClient.GetAsync("/health");
        return response.IsSuccessStatusCode;
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
