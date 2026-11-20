using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Pgvector.EntityFrameworkCore;
using System;
using System.IO;

namespace CloverBackoffice.Repository.Provider
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            // Path.Combine with separate segments so this resolves on Linux build
            // agents too, not just Windows.
            var apiProjectPath = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "..",
                "CloverBackoffice.API"));

            var builder = new ConfigurationBuilder();

            // Only when the directory is really there. SetBasePath on a missing directory
            // is not the harmless no-op it looks like: the first AddJsonFile builds a
            // PhysicalFileProvider over it, whose constructor throws
            // DirectoryNotFoundException regardless of optional: true. The migrations
            // bundle runs from an agent that downloaded the bundle and nothing else, so
            // ../CloverBackoffice.API does not exist there and this threw before the
            // bundle could open a connection. The exception carries only the path as its
            // message, which EF then reports as "Unable to create a 'DbContext'" — the
            // real cause is invisible unless you know where to look.
            if (Directory.Exists(apiProjectPath))
            {
                builder.SetBasePath(apiProjectPath);
            }

            IConfiguration configuration = builder
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)
                // Lets CI supply the connection string as
                // ConnectionStrings__DefaultConnection without a file on disk.
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // The bundle is invoked with --connection, which overrides anything set
                // here, so having no connection string at this point is normal on an agent
                // with no appsettings beside the bundle. Configuring the provider without
                // one keeps the model buildable and lets the caller supply the target.
                optionsBuilder.UseNpgsql(o => o.UseVector());
            }
            else
            {
                optionsBuilder.UseNpgsql(connectionString, o => o.UseVector());
            }

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
