using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// The query Eran runs for every engine team must translate on PostgreSQL (the in-memory
    /// provider would accept anything). ToQueryString needs no database connection.
    /// </summary>
    public class ManagerActionSqlTests
    {
        private static ApplicationDbContext Npgsql() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                // Same provider options as Program.cs.
                .UseNpgsql("Host=localhost;Database=none;Username=none;Password=none", o => o.UseVector())
                .Options);

        [Fact]
        public void Eran_open_actions_query_translates()
        {
            using var db = Npgsql();
            var sql = ManagerActionEranContext.OpenQuery(db, 294u).ToQueryString();

            Assert.Contains("\"ManagerActions\"", sql);
            Assert.Contains("\"IsDel\"", sql);
            Assert.Contains("\"UnitId\"", sql);
        }
    }
}
