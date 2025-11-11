using Testcontainers.PostgreSql;

namespace Outbox.Tests.Infrastructure;

/// <summary>
/// Elegant fixture that provides a PostgreSQL container for integration tests.
/// Automatically handles Docker endpoint configuration for both local WSL and CI environments.
/// Each test class gets its own isolated container instance via IClassFixture.
/// </summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>
    /// Gets the connection string for the PostgreSQL container.
    /// </summary>
    public string ConnectionString => _container?.GetConnectionString() 
        ?? throw new InvalidOperationException("Container not initialized. Call InitializeAsync first.");

    public async Task InitializeAsync()
    {
        _container = CreatePostgreSqlContainer();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a PostgreSQL container with environment-aware Docker endpoint configuration.
    /// Uses default Docker daemon in CI/CD, WSL endpoint for local development.
    /// </summary>
    private static PostgreSqlContainer CreatePostgreSqlContainer()
    {
        var builder = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("outbox_integration_test")
            .WithUsername("test_user")
            .WithPassword("test_pass");

        return IsLocalDevelopment()
            ? builder.WithDockerEndpoint("tcp://localhost:2375").Build()
            : builder.Build();
    }

    private static bool IsLocalDevelopment() =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
}
