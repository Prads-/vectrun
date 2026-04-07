using vectrun.Models.Clients;

namespace vectrun.Clients.Contracts;

internal interface IAIClient
{
    Task<AIChatResponse> SendAsync(
        AIChatRequest request,
        CancellationToken token);
}