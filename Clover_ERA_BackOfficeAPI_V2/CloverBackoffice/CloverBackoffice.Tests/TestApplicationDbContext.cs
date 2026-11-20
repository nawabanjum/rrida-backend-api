using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Tests
{
    /// <summary>
    /// ApplicationDbContext for the EF InMemory provider. Identical model, minus the
    /// pgvector columns, which only Npgsql can map.
    /// </summary>
    internal sealed class TestApplicationDbContext : ApplicationDbContext
    {
        public TestApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
            {
                foreach (var property in entity.GetProperties().ToList())
                {
                    if (property.ClrType.Namespace?.StartsWith("Pgvector", StringComparison.Ordinal) == true)
                        modelBuilder.Entity(entity.ClrType).Ignore(property.Name);
                }
            }
        }
    }
}
