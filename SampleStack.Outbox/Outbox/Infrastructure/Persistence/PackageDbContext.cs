using Microsoft.EntityFrameworkCore;
using Outbox.Infrastructure.PackageQueue;
using Outbox.Model;

namespace Outbox.Infrastructure.Persistence;

public class PackageDbContext : DbContext 
{
    public PackageDbContext(DbContextOptions<PackageDbContext> options) 
        : base(options)
    {
        
    }
    
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<Package> Packages { get; set; }
}