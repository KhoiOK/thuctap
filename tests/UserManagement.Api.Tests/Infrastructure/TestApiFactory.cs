using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UserManagement.Api.Data;
using UserManagement.Api.FileStorage;
using Xunit;
using ApiProgram = UserManagement.Api.Program;

namespace UserManagement.Api.Tests.Infrastructure;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> that swaps the production database
/// for a per-test SQL Server database created on the fly. Each instance is isolated so
/// tests can run in parallel without interfering with one another.
/// </summary>
public class TestApiFactory : WebApplicationFactory<ApiProgram>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"UserManagementDb_Test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration.
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(
                    $"Server=localhost;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True");
            });

            // Swap the real Cloudinary client for the in-memory double so the tests
            // never need Cloudinary credentials or network access.
            var cloudinaryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ICloudinaryStorage));
            if (cloudinaryDescriptor is not null)
            {
                services.Remove(cloudinaryDescriptor);
            }
            services.AddSingleton<ICloudinaryStorage, TestCloudinaryStorage>();
        });
    }

    public async Task InitializeAsync()
    {
        // Ensure the database exists. We can't use the host's DI yet because the factory
        // hasn't been built - create the database directly using master.
        using var setup = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        await setup.Database.ExecuteSqlRawAsync($"CREATE DATABASE [{DatabaseName}];");

        // Build the host to migrate the schema.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        using var setup = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        await setup.Database.ExecuteSqlRawAsync(
            $"IF DB_ID('{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END");
    }
}
