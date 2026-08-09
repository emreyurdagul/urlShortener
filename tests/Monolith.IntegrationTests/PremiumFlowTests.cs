using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Monolith.IntegrationTests;

/// <summary>
/// End-to-end tests against the real monolith + a real Postgres container. These
/// exercise the tier gating, vanity codes, self-serve upgrade (fresh token),
/// redirect, and the async click → analytics pipeline through actual HTTP.
/// </summary>
public sealed class PremiumFlowTests(MonolithFactory factory) : IClassFixture<MonolithFactory>
{
    private HttpClient NoRedirectClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"u{Guid.NewGuid():N}@test.local";

    private static async Task<string> RegisterAsync(HttpClient c, string email)
    {
        var res = await c.PostAsJsonAsync("/api/auth/register", new { email, password = "password123" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostAuthedAsync(HttpClient c, string url, object body, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await c.SendAsync(req);
    }

    private static async Task<string?> ErrorCodeOf(HttpResponseMessage res)
    {
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    [Fact]
    public async Task Full_journey_free_to_pro_with_vanity_and_analytics()
    {
        var c = NoRedirectClient();
        var token = await RegisterAsync(c, UniqueEmail());

        // Free tier: a 1-char code is locked behind a higher plan.
        var locked = await PostAuthedAsync(c, "/api/links", new { url = "https://example.com", codeLength = 1 }, token);
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
        Assert.Equal("code_length_locked", await ErrorCodeOf(locked));

        // Upgrade to Pro → a FRESH token carrying the new plan.
        var upgrade = await PostAuthedAsync(c, "/api/auth/upgrade", new { plan = "pro" }, token);
        upgrade.EnsureSuccessStatusCode();
        var upBody = await upgrade.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pro", upBody.GetProperty("plan").GetString());
        var proToken = upBody.GetProperty("token").GetString()!;

        // Pro: the 1-char code is now allowed, and it really is 1 character.
        var oneChar = await PostAuthedAsync(c, "/api/links", new { url = "https://example.com", codeLength = 1 }, proToken);
        Assert.Equal(HttpStatusCode.Created, oneChar.StatusCode);
        var shortCode = (await oneChar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        Assert.Equal(1, shortCode.Length);

        // Pro: a caller-chosen vanity code.
        var vanity = "brand-" + Guid.NewGuid().ToString("N")[..6];
        var van = await PostAuthedAsync(c, "/api/links", new { url = "https://example.com/x", code = vanity }, proToken);
        Assert.Equal(HttpStatusCode.Created, van.StatusCode);

        // The vanity code redirects (Host = localhost, the configured domain).
        var redirect = await c.GetAsync($"/{vanity}");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com/x", redirect.Headers.Location!.ToString());

        // The click reaches analytics off the redirect hot path (batched writer).
        Assert.True(await PollAnalyticsAsync(c, vanity) >= 1, "the redirect should have been counted");
    }

    private static async Task<long> PollAnalyticsAsync(HttpClient c, string code)
    {
        for (var i = 0; i < 25; i++)
        {
            var body = await (await c.GetAsync($"/api/analytics/{code}?domain=localhost"))
                .Content.ReadFromJsonAsync<JsonElement>();
            if (body.GetProperty("total").GetInt64() is var total && total >= 1) return total;
            await Task.Delay(200);
        }
        return 0;
    }

    [Fact]
    public async Task Duplicate_email_returns_email_taken()
    {
        var c = factory.CreateClient();
        var email = UniqueEmail();
        (await c.PostAsJsonAsync("/api/auth/register", new { email, password = "password123" })).EnsureSuccessStatusCode();

        var dup = await c.PostAsJsonAsync("/api/auth/register", new { email, password = "password123" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("email_taken", await ErrorCodeOf(dup));
    }

    [Fact]
    public async Task Wrong_password_returns_bad_credentials()
    {
        var c = factory.CreateClient();
        var email = UniqueEmail();
        (await c.PostAsJsonAsync("/api/auth/register", new { email, password = "password123" })).EnsureSuccessStatusCode();

        var login = await c.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-one" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal("bad_credentials", await ErrorCodeOf(login));
    }

    [Fact]
    public async Task Anonymous_create_is_free_tier()
    {
        var c = factory.CreateClient();

        var ok = await c.PostAsJsonAsync("/api/links", new { url = "https://example.com", codeLength = 5 });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var locked = await c.PostAsJsonAsync("/api/links", new { url = "https://example.com", codeLength = 4 });
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
        Assert.Equal("code_length_locked", await ErrorCodeOf(locked));
    }

    [Fact]
    public async Task Vanity_requires_pro_and_a_valid_slug()
    {
        var c = factory.CreateClient();
        var token = await RegisterAsync(c, UniqueEmail()); // free

        var forbidden = await PostAuthedAsync(c, "/api/links", new { url = "https://example.com", code = "myslug" }, token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("vanity_forbidden", await ErrorCodeOf(forbidden));

        var up = await PostAuthedAsync(c, "/api/auth/upgrade", new { plan = "pro" }, token);
        var proToken = (await up.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        // A reserved word collides with a real route, so it's rejected as invalid.
        var reserved = await PostAuthedAsync(c, "/api/links", new { url = "https://example.com", code = "api" }, proToken);
        Assert.Equal(HttpStatusCode.BadRequest, reserved.StatusCode);
        Assert.Equal("vanity_invalid", await ErrorCodeOf(reserved));
    }
}
