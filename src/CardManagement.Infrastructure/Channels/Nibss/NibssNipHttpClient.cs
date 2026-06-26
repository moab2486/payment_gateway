using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// HTTP-based implementation of INibssNipClient.
/// Uses IHttpClientFactory for resilient HTTP communication with NIBSS NIP API.
/// </summary>
public class NibssNipHttpClient : INibssNipClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public NibssNipHttpClient(HttpClient httpClient, IOptions<NipOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        var opts = options?.Value ?? throw new ArgumentNullException(nameof(options));

        _httpClient.BaseAddress = new Uri(opts.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
    }

    public async Task<NibssTransferResponse> SubmitTransferAsync(NibssTransferRequest request, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/nip/transfer", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<NibssTransferResponse>(JsonOptions, ct);
        return result ?? new NibssTransferResponse("96", "Empty response from NIBSS", null);
    }

    public async Task<NibssTransferResponse> QueryStatusAsync(string transactionReference, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"/api/nip/status/{transactionReference}", ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<NibssTransferResponse>(JsonOptions, ct);
        return result ?? new NibssTransferResponse("96", "Empty response from NIBSS", null);
    }
}
