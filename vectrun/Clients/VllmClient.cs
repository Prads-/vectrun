namespace vectrun.Clients;

using vectrun.Clients.Primitives;
using vectrun.Models.Clients;

internal class VllmAIClient : BaseOpenAiCompatibleAIClient
{
    public VllmAIClient(HttpClient httpClient, AIClientOptions options)
        : base(httpClient, options) { }
}