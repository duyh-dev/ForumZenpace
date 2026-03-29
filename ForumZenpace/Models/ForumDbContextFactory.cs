using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ForumZenpace.Models
{
    public class ForumDbContextFactory : IDesignTimeDbContextFactory<ForumDbContext>
    {
        public ForumDbContext CreateDbContext(string[] args)
        {
            var basePath = Directory.GetCurrentDirectory();
            if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
            {
                basePath = Path.Combine(basePath, "ForumZenpace");
            }

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection was not found.");

            var optionsBuilder = new DbContextOptionsBuilder<ForumDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            return new ForumDbContext(optionsBuilder.Options);
        }
    }
}
