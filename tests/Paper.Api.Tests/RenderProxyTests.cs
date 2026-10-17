using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Paper.Api.Tests;

public sealed class RenderProxyTests
{
    [Fact]
    public async Task ProductionLoginThroughRenderKeepsSecureCookiesAndAntiforgery()
    {
        using var factory = new ApiFactory();
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RENDER"] = "true" }));
        });
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Simulate Render's HTTP hop after HTTPS termination, including untrusted IP headers.
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.8");
        using var csrf = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var csrfCookie = Assert.Single(csrf.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", csrfCookie.ToLowerInvariant());
        client.DefaultRequestHeaders.Add("Cookie", csrfCookie.Split(';')[0]);
        var token = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "admin@example.test", password = "Integration-2026-Only" })
        };
        login.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        using var result = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var sessionCookie = Assert.Single(result.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", sessionCookie.ToLowerInvariant());
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", sessionCookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
    }

    [Fact]
    public async Task OutsideRenderForwardedSchemeDoesNotBypassHttpsRequirement()
    {
        using var factory = new ApiFactory();
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RENDER"] = "false" }));
        });
        using var client = production.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync("/api/auth/csrf")).StatusCode);
    }
}
