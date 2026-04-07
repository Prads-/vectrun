namespace vectrun.Clients;

using vectrun.Clients.Primitives;
using vectrun.Models.Clients;

internal class LlamaCppAIClient : BaseOpenAiCompatibleAIClient
{
    public LlamaCppAIClient(HttpClient httpClient, AIClientOptions options)
        : base(httpClient, options) { }
}