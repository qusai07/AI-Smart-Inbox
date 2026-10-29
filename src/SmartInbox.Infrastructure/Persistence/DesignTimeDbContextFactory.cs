using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartInbox.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef migrations add` run against this project alone, without building (or locking) the API:
///   dotnet ef migrations add Name -p src/SmartInbox.Infrastructure -s src/SmartInbox.Infrastructure -o Persistence/Migrations
/// The connection string is only used by commands that touch a database (e.g. `database update`).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SmartInboxDbContext>
{
    public SmartInboxDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__SmartInbox")
                         ?? "Server=localhost,1433;Database=SmartInbox;User Id=sa;Password=Your_strong_Passw0rd;TrustServerCertificate=True";
        return new SmartInboxDbContext(new DbContextOptionsBuilder<SmartInboxDbContext>().UseSqlServer(connection).Options);
    }
}
