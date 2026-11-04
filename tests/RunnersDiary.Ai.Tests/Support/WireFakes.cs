using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Azure.Core;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>One HTTP request as it left the OpenAI SDK pipeline: never sent to a network.</summary>
public sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);

/// <summary>
/// A <see cref="PipelineTransport"/> (the SDK's own <see cref="HttpClientPipelineTransport"/> over an in-memory handler) that records
/// every request and answers from a queue of canned responses. The handler never opens a socket.
/// </summary>
public sealed class CapturingTransport : IDisposable
{
    private readonly Handler _handler;
    private readonly HttpClient _http;

    public CapturingTransport()
    {
        _handler = new Handler();
        _http = new HttpClient(_handler);
        Transport = new HttpClientPipelineTransport(_http);
    }

    public PipelineTransport Transport { get; }

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_handler.Requests)
            {
                return [.. _handler.Requests];
            }
        }
    }

    /// <summary>A chat completion with <paramref name="content"/> as the assistant message.</summary>
    public CapturingTransport RespondCompletion(string content, int promptTokens = 5000, int completionTokens = 600, string model = "gpt-5.4-mini-2026-03-17")
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = "chatcmpl-test",
            ["object"] = "chat.completion",
            ["created"] = 1_790_000_000,
            ["model"] = model,
            ["choices"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["index"] = 0,
                    ["message"] = new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = content, ["refusal"] = null },
                    ["finish_reason"] = "stop",
                },
            },
            ["usage"] = new Dictionary<string, object?>
            {
                ["prompt_tokens"] = promptTokens,
                ["completion_tokens"] = completionTokens,
                ["total_tokens"] = promptTokens + completionTokens,
            },
        });
        return Respond(HttpStatusCode.OK, body);
    }

    public CapturingTransport Respond(HttpStatusCode status, string body = "{}", IReadOnlyDictionary<string, string>? headers = null)
    {
        lock (_handler.Responses)
        {
            _handler.Responses.Enqueue((status, body, headers ?? new Dictionary<string, string>()));
        }

        return this;
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public Queue<(HttpStatusCode Status, string Body, IReadOnlyDictionary<string, string> Headers)> Responses { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body;
            using (var reader = new StreamReader(request.Content?.ReadAsStream(cancellationToken) ?? Stream.Null))
            {
                body = reader.ReadToEnd();
            }

            lock (Requests)
            {
                Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            }

            (HttpStatusCode Status, string Body, IReadOnlyDictionary<string, string> Headers) next;
            lock (Responses)
            {
                if (!Responses.TryDequeue(out next))
                {
                    throw new InvalidOperationException("The capturing transport has no response left.");
                }
            }

            var response = new HttpResponseMessage(next.Status) { Content = new StringContent(next.Body, Encoding.UTF8, "application/json"), RequestMessage = request };
            foreach (var (name, value) in next.Headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        }
    }
}

/// <summary>A <see cref="TokenCredential"/> that records the requested scopes and returns a dummy token, or throws <see cref="Failure"/>.</summary>
public sealed class RecordingCredential(Exception? failure = null) : TokenCredential
{
    public const string Token = "fake-access-token";

    public Exception? Failure { get; } = failure;

    public List<string[]> RequestedScopes { get; } = [];

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        lock (RequestedScopes)
        {
            RequestedScopes.Add(requestContext.Scopes);
        }

        return Failure is null ? new AccessToken(Token, DateTimeOffset.UtcNow.AddHours(1)) : throw Failure;
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}

/// <summary>Provider errors as the OpenAI SDK raises them, built over an in-memory pipeline (a real <see cref="PipelineResponse"/>, no network).</summary>
public static class ProviderErrors
{
    /// <summary>A <see cref="System.ClientModel.ClientResultException"/> for an HTTP 429 with <paramref name="headers"/> (e.g. Retry-After).</summary>
    public static System.ClientModel.ClientResultException Throttled(IReadOnlyDictionary<string, string>? headers = null) =>
        Status(HttpStatusCode.TooManyRequests, headers);

    public static System.ClientModel.ClientResultException Status(HttpStatusCode status, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var transport = new CapturingTransport().Respond(status, """{"error":{"code":"x","message":"x"}}""", headers);
        var pipeline = ClientPipeline.Create(new ClientPipelineOptions { Transport = transport.Transport, RetryPolicy = new ClientRetryPolicy(0) }, [], [], []);
        var message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri("https://res.openai.azure.com/openai/v1/chat/completions");
        pipeline.Send(message);
        var response = message.Response!;
        response.BufferContent();
        return new System.ClientModel.ClientResultException(response);
    }
}
