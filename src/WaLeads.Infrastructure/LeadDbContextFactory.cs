using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WaLeads.Infrastructure;

public sealed class LeadDbContextFactory : IDesignTimeDbContextFactory<LeadDbContext>
{
    public LeadDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__LeadDatabase")
            ?? "Host=localhost;Database=lead_automation;Username=lead_automation;Password=design-time-only";
        var options = new DbContextOptionsBuilder<LeadDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new LeadDbContext(options);
    }
}
