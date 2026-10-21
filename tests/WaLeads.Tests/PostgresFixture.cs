using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using WaLeads.Infrastructure;

namespace WaLeads.Tests;

[CollectionDefinition("Qualification PostgreSQL", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Qualification PostgreSQL";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("lead_automation")
        .WithUsername("lead_automation")
        .WithPassword("integration-test-password")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_TESTCONTAINERS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await _container.StartAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "02-schema.sql"));
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO tenants (name, phone_number_id, business_context)
                VALUES ('Migration preservation test', 'phase2-migration-fixture', 'Verified services only');
                INSERT INTO leads (tenant_id, wa_id, profile_name)
                SELECT id, 'phase2-existing-lead', 'Existing phase 1 lead'
                FROM tenants WHERE phone_number_id = 'phase2-migration-fixture';
                INSERT INTO messages (lead_id, wa_message_id, direction, type, body, received_at)
                SELECT id, 'phase2-existing-message', 'inbound', 'text', 'Existing phase 1 message', now() - interval '1 minute'
                FROM leads WHERE wa_id = 'phase2-existing-lead';
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        Assert.True(await db.Messages.AnyAsync(message =>
            message.WaMessageId == "phase2-existing-message" && message.ProcessedAt == null));
        Assert.Equal(70, await db.Tenants
            .Where(tenant => tenant.PhoneNumberId == "phase2-migration-fixture")
            .Select(tenant => tenant.HotThreshold)
            .SingleAsync());
    }

    public LeadDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<LeadDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
