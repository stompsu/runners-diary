using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;

namespace RunnersDiary.Api.Tests.Infrastructure;

/// <summary>Provider errors as the OpenAI SDK raises them (a real <see cref="PipelineResponse"/> from an in-memory pipeline; no network).</summary>
internal static class ProviderErrors
{
    public static ClientResultException Throttled(IReadOnlyDictionary<string, string>? headers = null)
    {
        using var handler = new CannedHandler(HttpStatusCode.TooManyRequests, headers ?? new Dictionary<string, string>());
        using var http = new HttpClient(handler);
        var pipeline = ClientPipeline.Create(
            new ClientPipelineOptions { Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) }, [], [], []);
        var message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri("https://example.invalid/openai/v1/chat/completions");
        pipeline.Send(message);
        var response = message.Response!;
        response.BufferContent();
        return new ClientResultException(response);
    }

    private sealed class CannedHandler(HttpStatusCode status, IReadOnlyDictionary<string, string> headers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"error":{"code":"429","message":"Rate limit"}}""", Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        }
    }
}
