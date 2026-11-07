using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vroo.Data;

// Lets `dotnet ef migrations add` run without a database or the full host.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VrooDbContext>
{
    public VrooDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<VrooDbContext>()
            .UseNpgsql("Host=localhost;Database=vroo_design")
            .Options;
        return new VrooDbContext(options);
    }
}
