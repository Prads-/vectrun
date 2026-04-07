namespace vectrun.Clients.Primitives;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using vectrun.Clients.Contracts;
using vectrun.Models.Clients;

internal abstract class BaseAIClient : IAIClient
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    protected BaseAIClient(HttpClient httpClient, AIClientOptions options)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    protected HttpClient HttpClient { get; }
    protected AIClientOptions Options { get; }

    public abstract Task<AIChatResponse> SendAsync(
        AIChatRequest request,
        CancellationToken cancellationToken = default);

    protected async Task<JsonElement> PostJsonAsync<TRequest>(
        string path,
        TRequest payload,
        CancellationToken token)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, BuildUri(path))
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };

        ApplyHeaders(msg);

        using var res = await HttpClient.SendAsync(
            msg,
            HttpCompletionOption.ResponseHeadersRead,
            token);

        var text = await res.Content.ReadAsStringAsync(token);

        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException(text);

        return JsonSerializer.Deserialize<JsonElement>(text, JsonOptions);
    }

    protected virtual void ApplyHeaders(HttpRequestMessage request) { }

    protected Uri BuildUri(string path)
    {
        return new Uri(Options.Endpoint.TrimEnd('/') + path);
    }
}