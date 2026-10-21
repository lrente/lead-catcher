namespace WaLeads.Tests;

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_TESTCONTAINERS"), "1", StringComparison.Ordinal))
        {
            Skip = "Set RUN_TESTCONTAINERS=1 to run PostgreSQL Testcontainers integration tests.";
        }
    }
}
