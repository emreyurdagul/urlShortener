using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Monolith.IntegrationTests;

/// <summary>
/// End-to-end tests for the newer features against the real monolith + Postgres:
/// refresh-token rotation/revocation, link edit/delete (with cache invalidation),
/// analytics detail, and pagination.
/// </summary>
[Collection("monolith")]
public sealed class LinkAndAuthTests(MonolithFactory factory)
{
    private HttpClient NoRedirect() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"u{Guid.NewGuid():N}@test.local";

    private static async Task<(string Access, string Refresh)> RegisterAsync(HttpClient c)
    {
        var res = await c.PostAsJsonAsync("/api/auth/register", new { email = UniqueEmail(), password = "password123" });
        res.EnsureSuccessStatusCode();
        var b = await res.Content.ReadFromJsonAsync<JsonElement>();
        return (b.GetProperty("token").GetString()!, b.GetProperty("refreshToken").GetString()!);
    }

    private static async Task<HttpResponseMessage> Authed(HttpClient c, HttpMethod method, string url, string token, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return await c.SendAsync(req);
    }

    private static async Task<string> CreateAsync(HttpClient c, string token, string url)
    {
        var res = await Authed(c, HttpMethod.Post, "/api/links", token, new { url });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Refresh_rotates_and_revokes()
    {
        var c = factory.CreateClient();
        var (_, refresh) = await RegisterAsync(c);

        // Refresh → new tokens.
        var r1 = await c.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var newRefresh = (await r1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(refresh, newRefresh); // rotated

        // The OLD refresh token is now revoked.
        var reused = await c.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

        // Logout revokes the current one too.
        var logout = await c.PostAsJsonAsync("/api/auth/logout", new { refreshToken = newRefresh });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var afterLogout = await c.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = newRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Edit_and_delete_take_effect_on_redirect()
    {
        var c = NoRedirect();
        var (access, _) = await RegisterAsync(c);
        var code = await CreateAsync(c, access, "https://before.example");

        var before = await c.GetAsync($"/{code}");
        Assert.Equal("https://before.example/", before.Headers.Location!.ToString());

        var edit = await Authed(c, HttpMethod.Put, $"/api/links/{code}?domain=localhost", access, new { url = "https://after.example" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var after = await c.GetAsync($"/{code}");
        Assert.Equal("https://after.example/", after.Headers.Location!.ToString());

        var del = await Authed(c, HttpMethod.Delete, $"/api/links/{code}?domain=localhost", access);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var gone = await c.GetAsync($"/{code}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Analytics_detail_reports_daily_and_referrers()
    {
        var c = NoRedirect();
        var (access, _) = await RegisterAsync(c);
        var code = await CreateAsync(c, access, "https://example.com");

        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/{code}"))
        {
            req.Headers.Referrer = new Uri("https://news.example/story");
            await c.SendAsync(req);
        }

        JsonElement body = default;
        for (var i = 0; i < 25; i++)
        {
            body = await (await c.GetAsync($"/api/analytics/{code}?domain=localhost")).Content.ReadFromJsonAsync<JsonElement>();
            if (body.GetProperty("total").GetInt64() >= 1) break;
            await Task.Delay(200);
        }
        Assert.True(body.GetProperty("total").GetInt64() >= 1);
        Assert.True(body.TryGetProperty("daily", out var daily) && daily.ValueKind == JsonValueKind.Array);
        Assert.True(body.TryGetProperty("topReferrers", out var refs) && refs.GetArrayLength() >= 1);
        Assert.Contains("news.example", refs[0].GetProperty("referer").GetString());
    }

    [Fact]
    public async Task Links_are_paginated()
    {
        var c = factory.CreateClient();
        var (access, _) = await RegisterAsync(c);
        for (var i = 0; i < 3; i++)
            await CreateAsync(c, access, $"https://example.com/{i}");

        var p1 = await (await Authed(c, HttpMethod.Get, "/api/links?page=1&pageSize=2", access))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, p1.GetProperty("total").GetInt64());
        Assert.Equal(2, p1.GetProperty("links").GetArrayLength());
        Assert.True(p1.GetProperty("hasMore").GetBoolean());

        var p2 = await (await Authed(c, HttpMethod.Get, "/api/links?page=2&pageSize=2", access))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, p2.GetProperty("links").GetArrayLength());
        Assert.False(p2.GetProperty("hasMore").GetBoolean());
    }
}
