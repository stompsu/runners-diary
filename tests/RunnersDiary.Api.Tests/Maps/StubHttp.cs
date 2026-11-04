using System.Net;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace RunnersDiary.Api.Tests.Maps;

/// <summary>A message handler that records requests and answers from a delegate. No network.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return respond(request);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>Hands out a fixed token and remembers the scopes asked for.</summary>
internal sealed class FakeCredential(string token = "entra-token") : TokenCredential
{
    public List<string> Scopes { get; } = [];

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Record(requestContext);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(Record(requestContext));

    private AccessToken Record(TokenRequestContext context)
    {
        Scopes.AddRange(context.Scopes);
        return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
    }
}

/// <summary>Captures every formatted log line so tests can assert nothing sensitive was written.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
}
