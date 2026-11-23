using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UserManagement.Api.Data;
using UserManagement.Api.FileStorage;
using UserManagement.Api.Middleware;
using UserManagement.Api.Ocr;
using UserManagement.Api.Services;
using UserManagement.Web.Services;
using Xunit;
using ApiProgram = UserManagement.Api.Program;

namespace UserManagement.Web.Tests.Infrastructure;

/// <summary>
/// End-to-end test harness that wires the Web MVC project to a Backend API instance
/// with a per-test SQL Server database and per-test file storage directory. The API
/// is launched as a real Kestrel host on a random localhost port; the Web host is
/// then pointed at that address via <c>Api:BaseUrl</c>.
/// </summary>
public class IntegrationTestHarness : IAsyncLifetime, IDisposable
{
    public string DatabaseName { get; } = $"UserManagementDb_It_{Guid.NewGuid():N}";
    public string FileStorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"UserManagement_It_{Guid.NewGuid():N}");

    private IHost? _apiHost;
    private int _apiPort;
    public Uri ApiBaseAddress => new($"http://localhost:{_apiPort}");

    public WebApplicationFactory<Program> Web { get; private set; } = null!;
    public HttpClient WebClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(FileStorageRoot);

        // Find a free TCP port to host the API.
        _apiPort = FindFreePort();

        // ---------- Boot the API as a real Kestrel host ----------
        var apiConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Server=localhost;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True",
                ["FileStorage:RootPath"] = FileStorageRoot,
                ["FileStorage:MaxFileSizeBytes"] = "5242880",
                ["FileStorage:MaxRequestBodyBytes"] = "11534336",
                ["FileStorage:MaxPixelSize"] = "8000",
                ["AllowedOrigins:0"] = "http://localhost:0",
                ["Logging:LogLevel:Default"] = "Warning",
                ["urls"] = $"http://localhost:{_apiPort}"
            })
            .Build();

        // Build the API host by invoking its top-level Main via WebApplication pattern.
        // The trick: we need the same configuration the API uses in production, including
        // the file storage, CORS, and database. We re-construct the builder here so we
        // can target a different port and pass the test config without sharing state.
        _apiHost = await BuildApiHostAsync(apiConfig);
        await _apiHost.StartAsync();

        // Create the database and apply migrations.
        using (var scope = _apiHost.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
        }

        // ---------- Boot the Web host pointed at the API ----------
        // Set env vars BEFORE the Web host is built. `WebApplication.CreateBuilder` in the
        // Web's Program.cs loads env vars AFTER appsettings.json, so they take precedence.
        Environment.SetEnvironmentVariable("Api__BaseUrl", ApiBaseAddress.ToString().TrimEnd('/'));
        Environment.SetEnvironmentVariable("Api__TimeoutSeconds", "30");
        Environment.SetEnvironmentVariable("Display__TimeZoneId", "SE Asia Standard Time");
        // Disable HTTPS redirection in the test Web host.
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTPS_PORT", "");
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://localhost:0");
        Environment.SetEnvironmentVariable("DISABLE_HTTPS_REDIRECT", "true");

        Web = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
        WebClient = Web.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true,
            HandleCookies = true
        });
    }

    /// <summary>
    /// Builds the API IHost using the same configuration as the API's top-level
    /// Program.cs. We can't invoke Program.Main directly because of port conflicts,
    /// so we re-construct the WebApplication with the same services and middleware.
    /// </summary>
    private static async Task<IHost> BuildApiHostAsync(IConfiguration config)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddConfiguration(config);
        // Use the same options the API does.
        var maxRequestBodyBytes = builder.Configuration.GetValue<long?>("FileStorage:MaxRequestBodyBytes") ?? 11_534_336L;

        builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));
        builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
        builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection(CloudinaryOptions.SectionName));
        builder.Services.AddSingleton<ICloudinaryStorage, TestCloudinaryStorage>();
        builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection(OcrOptions.SectionName));
        builder.Services.AddSingleton<IPaddleOcrClient, FakePaddleOcrClient>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ImageValidator>();
        builder.Services.AddSingleton<OcrNormalizer>();

        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default must be configured.");
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
                sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
            });
        });
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IIdentityDocumentService, IdentityDocumentService>();
        builder.Services.AddScoped<IOcrService, OcrService>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddScoped<ModelStateValidationFilter>();

        // Add controllers from the API assembly (which is not the calling assembly in
        // this test harness). We discover them via ApplicationPart.
        var apiAssembly = typeof(ApiProgram).Assembly;
        builder.Services.AddControllers(options =>
            {
                options.Filters.AddService<ModelStateValidationFilter>();
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(
                    System.Text.Json.JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.SuppressModelStateInvalidFilter = true;
            })
            .AddApplicationPart(apiAssembly);

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "UserManagement.Api",
                Version = "v1"
            });
        });
        builder.WebHost.ConfigureKestrel(opts => { opts.Limits.MaxRequestBodySize = maxRequestBodyBytes; });

        var app = builder.Build();

        // Apply the same pipeline as Program.cs.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRouting();
        app.UseCors(cors =>
        {
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
            if (allowedOrigins.Length > 0)
            {
                cors.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
        });
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // Apply pending migrations and start.
        return app;
    }

    public async Task DisposeAsync()
    {
        WebClient?.Dispose();
        if (Web is not null) await Web.DisposeAsync();
        if (_apiHost is not null) await _apiHost.StopAsync();
        _apiHost?.Dispose();
        await DropDatabaseAsync();
        try
        {
            if (Directory.Exists(FileStorageRoot))
            {
                Directory.Delete(FileStorageRoot, recursive: true);
            }
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        WebClient?.Dispose();
        Web?.Dispose();
        _apiHost?.Dispose();

        // Clear env vars we set so they don't leak across tests.
        Environment.SetEnvironmentVariable("Api__BaseUrl", null);
        Environment.SetEnvironmentVariable("Api__TimeoutSeconds", null);
        Environment.SetEnvironmentVariable("Display__TimeZoneId", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTPS_PORT", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);
        Environment.SetEnvironmentVariable("DISABLE_HTTPS_REDIRECT", null);
    }

    private async Task DropDatabaseAsync()
    {
        try
        {
            using var setup = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True")
                .Options);
            await setup.Database.ExecuteSqlRawAsync(
                $"IF DB_ID('{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END");
        }
        catch { /* ignore */ }
    }

    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
