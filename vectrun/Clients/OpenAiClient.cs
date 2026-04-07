namespace vectrun.Clients;

using vectrun.Clients.Primitives;
using vectrun.Models.Clients;

internal class OpenAiAIClient : BaseOpenAiCompatibleAIClient
{
    public OpenAiAIClient(HttpClient httpClient, AIClientOptions options)
        : base(httpClient, options) { }
}