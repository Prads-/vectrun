namespace vectrun.tests.Helpers;

using System.Net;

internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseJson;
    private readonly HttpStatusCode _status;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public MockHttpMessageHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responseJson = responseJson;
        _status = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        LastRequestBody = request.Content is not null
            ? await request.Content.ReadAsStringAsync(ct)
            : null;

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_responseJson, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
