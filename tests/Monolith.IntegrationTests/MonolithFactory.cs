using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Monolith.IntegrationTests;

/// <summary>
/// Boots the WHOLE monolith against a real, throwaway Postgres in a container
/// (Testcontainers) — a genuine end-to-end harness, not mocks. The app runs its
/// real startup migration against the container, so tests exercise the same code
/// path that runs in production.
/// </summary>
public sealed class MonolithFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("shortener")
        .WithUsername("shortener")
        .WithPassword("shortener")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Db", _postgres.GetConnectionString());
        builder.UseSetting("JWT_SECRET", "integration-test-secret-key-at-least-32-bytes");
        builder.UseSetting("LINK_DOMAINS", "localhost");
    }

    // Explicit interface implementation: WebApplicationFactory already defines a
    // DisposeAsync() returning ValueTask, which would clash with xUnit's
    // IAsyncLifetime.DisposeAsync() (Task) if implemented implicitly.
    async Task IAsyncLifetime.InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>Shares one factory (one Postgres container) across all test classes,
/// so they run sequentially against a single container rather than spinning one
/// each in parallel.</summary>
[CollectionDefinition("monolith")]
public sealed class MonolithCollection : ICollectionFixture<MonolithFactory>;
