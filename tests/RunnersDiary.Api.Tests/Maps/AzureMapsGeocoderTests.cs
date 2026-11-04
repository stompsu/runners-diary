using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Tests.Maps;

public sealed class AzureMapsGeocoderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Sample = """
        {"type":"FeatureCollection","features":[
          {"type":"Feature","properties":{"type":"PopulatedPlace","address":{"formattedAddress":"Gliwice, Silesia, Poland","locality":"Gliwice"}},"geometry":{"type":"Point","coordinates":[18.6714,50.2945]}},
          {"type":"Feature","properties":{"type":"Address","address":{"formattedAddress":"  Rynek 1, Gliwice  "}},"geometry":{"type":"Point","coordinates":[18.67,50.29]}},
          {"type":"Feature","properties":{"type":"PointOfInterest","address":{"locality":"Zabrze"}},"geometry":{"type":"Point","coordinates":[18.78,50.32]}},
          {"type":"Feature","properties":{"address":{"formattedAddress":"Bad latitude"}},"geometry":{"type":"Point","coordinates":[18.0,95.0]}},
          {"type":"Feature","properties":{"address":{"formattedAddress":"No geometry"}}},
          {"type":"Feature","properties":{"address":{}},"geometry":{"type":"Point","coordinates":[18.0,50.0]}},
          {"type":"Feature","properties":{"type":"Neighborhood","address":{"formattedAddress":"Sośnica"}},"geometry":{"type":"Point","coordinates":[18.62,50.27]}},
          {"type":"Feature","properties":{"type":"AdminDivision1","address":{"formattedAddress":"Silesia"}},"geometry":{"type":"Point","coordinates":[19.0,50.5]}},
          {"type":"Feature","properties":{"type":"CountryRegion","address":{"formattedAddress":"Sixth valid"}},"geometry":{"type":"Point","coordinates":[19.1,52.0]}}
        ]}
        """;

    private static AzureMapsOptions Options() => new() { AccountClientId = "account-unique-id" };

    private static (AzureMapsGeocoder Geocoder, StubHandler Handler, FakeCredential Credential, CapturingLogger<AzureMapsGeocoder> Log) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var credential = new FakeCredential();
        var log = new CapturingLogger<AzureMapsGeocoder>();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://eu.atlas.microsoft.com/") };
        return (new AzureMapsGeocoder(http, credential, Options(), log), handler, credential, log);
    }

    [Fact]
    public async Task The_request_targets_the_geography_endpoint_with_the_entra_token_and_the_account_client_id()
    {
        var (geocoder, handler, credential, _) = Create(_ => StubHandler.Json(Sample));

        await geocoder.SearchAsync("Gliwice Rynek & more", 50.29, 18.67, Ct);

        var request = Assert.Single(handler.Requests).Request;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("eu.atlas.microsoft.com", request.RequestUri!.Host);
        Assert.Equal("/geocode", request.RequestUri.AbsolutePath);
        Assert.Contains("api-version=2026-01-01", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("top=5", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("query=Gliwice%20Rynek%20%26%20more", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("coordinates=18.67,50.29", request.RequestUri.Query, StringComparison.Ordinal); // lon,lat, invariant culture
        Assert.Equal("Bearer entra-token", request.Headers.Authorization?.ToString());
        Assert.Equal("account-unique-id", Assert.Single(request.Headers.GetValues("x-ms-client-id")));
        Assert.Equal(["https://atlas.microsoft.com/.default"], credential.Scopes);
        Assert.False(request.Headers.Contains("Ocp-Apim-Subscription-Key"));
    }

    [Fact]
    public async Task Without_a_bias_no_coordinates_are_sent()
    {
        var (geocoder, handler, _, _) = Create(_ => StubHandler.Json(Sample));

        await geocoder.SearchAsync("Gliwice", null, null, Ct);

        Assert.DoesNotContain("coordinates", handler.Requests[0].Request.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Features_map_to_neutral_places_dropping_unusable_ones_and_capping_at_five()
    {
        var (geocoder, _, _, _) = Create(_ => StubHandler.Json(Sample));

        var places = await geocoder.SearchAsync("Gliwice", null, null, Ct);

        Assert.Equal(5, places.Count);
        Assert.Equal(new GeocodedPlace("Gliwice, Silesia, Poland", 50.2945, 18.6714, "locality"), places[0]);
        Assert.Equal(new GeocodedPlace("Rynek 1, Gliwice", 50.29, 18.67, "address"), places[1]);
        Assert.Equal(new GeocodedPlace("Zabrze", 50.32, 18.78, null), places[2]); // locality fallback; POIs have no neutral type
        Assert.Equal("neighborhood", places[3].Type);
        Assert.Equal("region", places[4].Type);
        Assert.DoesNotContain(places, p => p.Name is "Bad latitude" or "No geometry");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"features\":null}")]
    [InlineData("{\"features\":[]}")]
    public async Task An_unexpected_or_empty_document_yields_no_places(string json)
    {
        var (geocoder, _, _, _) = Create(_ => StubHandler.Json(json));

        Assert.Empty(await geocoder.SearchAsync("Gliwice", null, null, Ct));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_provider_error_status_is_an_exception_that_logs_only_the_status(HttpStatusCode status)
    {
        var (geocoder, _, _, log) = Create(_ => StubHandler.Json("{\"error\":{\"message\":\"provider detail\"}}", status));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => geocoder.SearchAsync("secret place name", 50.123456, 18.654321, Ct));

        Assert.Equal(status, ex.StatusCode);
        Assert.DoesNotContain("secret place name", ex.ToString(), StringComparison.Ordinal);
        var line = Assert.Single(log.Lines);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Neither_the_query_nor_the_bias_nor_the_tokens_are_ever_logged()
    {
        var (geocoder, _, _, log) = Create(_ => StubHandler.Json(Sample));
        var (failing, _, _, failLog) = Create(_ => StubHandler.Json("{}", HttpStatusCode.BadGateway));

        await geocoder.SearchAsync("secret place name", 50.123456, 18.654321, Ct);
        await Assert.ThrowsAsync<HttpRequestException>(() => failing.SearchAsync("secret place name", 50.123456, 18.654321, Ct));

        foreach (var line in log.Lines.Concat(failLog.Lines))
        {
            Assert.DoesNotContain("secret", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("50.123", line, StringComparison.Ordinal);
            Assert.DoesNotContain("entra-token", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Malformed_json_is_an_exception_not_an_empty_answer()
    {
        var (geocoder, _, _, _) = Create(_ => StubHandler.Json("<html>nope"));

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => geocoder.SearchAsync("Gliwice", null, null, Ct));
    }

    [Fact]
    public async Task A_blank_query_is_rejected_before_any_call()
    {
        var (geocoder, handler, _, _) = Create(_ => StubHandler.Json(Sample));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => geocoder.SearchAsync("  ", null, null, Ct));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void It_is_available_and_the_default_geocoder_is_not()
    {
        Assert.True(Create(_ => StubHandler.Json(Sample)).Geocoder.IsAvailable);
        Assert.False(new NoGeocoder().IsAvailable);
        _ = NullLogger<AzureMapsGeocoder>.Instance;
    }
}
