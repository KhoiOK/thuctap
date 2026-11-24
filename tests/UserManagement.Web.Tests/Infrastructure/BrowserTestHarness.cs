using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
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
/// Harness for BROWSER-level tests (Playwright).
///
/// Unlike <see cref="IntegrationTestHarness"/>, the Web host here runs on a REAL
/// Kestrel port so a browser can reach it. The Backend API runs on another real port.
/// Both use per-test doubles:
/// - <see cref="TestCloudinaryStorage"/> instead of the real Cloudinary account,
/// - <see cref="FakePaddleOcrClient"/> instead of the PaddleOCR service.
///
/// Nothing in this harness touches production data, real cloud storage, or any
/// external service.
/// </summary>
public class BrowserTestHarness : IAsyncLifetime, IDisposable
{
    public string DatabaseName { get; } = $"UserManagementDb_Br_{Guid.NewGuid():N}";
    public string FileStorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"UserManagement_Br_{Guid.NewGuid():N}");

    /// <summary>Directory where screenshots and traces are written.</summary>
    public string ArtifactDirectory { get; } = Path.Combine(
        Path.GetTempPath(), "UserManagement_BrowserArtifacts", Guid.NewGuid().ToString("N"));

    private IHost? _apiHost;
    private IHost? _webHost;
    private int _apiPort;
    private int _webPort;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public Uri WebBaseAddress => new($"http://localhost:{_webPort}");
    public Uri ApiBaseAddress => new($"http://localhost:{_apiPort}");

    /// <summary>Service provider of the Backend API host (for direct DB assertions).</summary>
    public IServiceProvider ApiServices => _apiHost?.Services
        ?? throw new InvalidOperationException("The API host has not been started yet.");

    /// <summary>Shared Playwright instance (browser binaries are installed once per run).</summary>
    public IPlaywright PlaywrightInstance => _playwright
        ?? throw new InvalidOperationException("Playwright has not been started yet.");

    public IBrowser Browser => _browser
        ?? throw new InvalidOperationException("The browser has not been started yet.");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(FileStorageRoot);
        Directory.CreateDirectory(ArtifactDirectory);

        _apiPort = FindFreePort();
        _webPort = FindFreePort();

        // ---------------- Backend API on a real Kestrel port ----------------
        var apiConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Server=localhost;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True",
                ["FileStorage:RootPath"] = FileStorageRoot,
                ["FileStorage:MaxFileSizeBytes"] = "5242880",
                ["FileStorage:MaxRequestBodyBytes"] = "11534336",
                ["FileStorage:MaxPixelSize"] = "8000",
                ["AllowedOrigins:0"] = $"http://localhost:{_webPort}",
                ["Ocr:Enabled"] = "true",
                ["Ocr:BaseUrl"] = "http://localhost:8100",
                ["Ocr:MinConfidence"] = "0.60",
                ["Ocr:HighConfidence"] = "0.85",
                ["Logging:LogLevel:Default"] = "Warning",
                ["urls"] = $"http://localhost:{_apiPort}"
            })
            .Build();

        _apiHost = await BuildApiHostAsync(apiConfig);
        await _apiHost.StartAsync();

        using (var scope = _apiHost.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
        }

        // ---------------- Web MVC on a real Kestrel port ----------------
        // The Web app reads its configuration from appsettings.json + environment
        // variables, so the API base URL is injected through the environment.
        Environment.SetEnvironmentVariable("Api__BaseUrl", ApiBaseAddress.ToString().TrimEnd('/'));
        Environment.SetEnvironmentVariable("Api__TimeoutSeconds", "30");
        Environment.SetEnvironmentVariable("Display__TimeZoneId", "SE Asia Standard Time");
        Environment.SetEnvironmentVariable("DISABLE_HTTPS_REDIRECT", "true");
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTPS_PORT", "");
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://localhost:{_webPort}");

        // The test assembly lives in its own output folder, so the WebRootPath would
        // default to a non-existent "wwwroot" there. Point the host at the real Web
        // project so app.css / site.js / quick-create-user.js are served exactly as
        // they are in development.
        var webRoot = ResolveWebRoot();

        var webBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = webRoot,
            WebRootPath = Path.Combine(webRoot, "wwwroot")
        });
        webBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:BaseUrl"] = ApiBaseAddress.ToString().TrimEnd('/'),
            ["Api:TimeoutSeconds"] = "30",
            ["Display:TimeZoneId"] = "SE Asia Standard Time"
        });
        webBuilder.Environment.EnvironmentName = "Development";
        webBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        webBuilder.WebHost.UseUrls($"http://localhost:{_webPort}");

        // The views live in the UserManagement.Web assembly (compiled Razor views) and
        // the static assets live in that project's wwwroot folder. The test assembly
        // must therefore register the Web assembly as an application part, otherwise
        // no controller or view can be found.
        webBuilder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(Program).Assembly);

        webBuilder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "RequestVerificationToken";
            options.Cookie.Name = "UserManagement.Antiforgery";
        });
        webBuilder.Services.AddHttpClient<IApiClient, ApiClient>(client =>
        {
            client.BaseAddress = new Uri(ApiBaseAddress.ToString().TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });
        webBuilder.Services.AddScoped<IUsersApiClient, UsersApiClient>();
        webBuilder.Services.Configure<DisplayOptions>(webBuilder.Configuration.GetSection("Display"));

        var webApp = webBuilder.Build();
        webApp.UseStaticFiles();
        webApp.UseRouting();
        webApp.UseAuthorization();
        webApp.MapGet("/", context =>
        {
            context.Response.Redirect("/Users");
            return Task.CompletedTask;
        });
        webApp.MapControllerRoute(
            name: "default",
            pattern: "{controller=Users}/{action=Index}/{id?}");

        _webHost = webApp;
        await webApp.StartAsync();

        // ---------------- Playwright ----------------
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    /// <summary>
    /// Creates an isolated browser context (fresh cookies + storage) and a page.
    /// Every test gets its own context so sessions never leak between tests.
    /// </summary>
    public async Task<(IBrowserContext Context, IPage Page)> NewPageAsync(
        int width = 1440, int height = 900)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "vi-VN"
        });
        var page = await context.NewPageAsync();
        return (context, page);
    }

    /// <summary>
    /// Locates the Web project's content root (the folder that holds "Views" and
    /// "wwwroot"). The test assembly runs from its own output folder, so the default
    /// content root would not contain the views or the static assets.
    /// </summary>
    private static string ResolveWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        // Walk up until we find the repository root (the folder that holds "src").
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        var webProject = dir is null
            ? null
            : Path.Combine(dir.FullName, "src", "UserManagement.Web");

        if (webProject is null || !Directory.Exists(webProject))
        {
            throw new InvalidOperationException(
                "Could not locate the UserManagement.Web project folder (expected <repo>/src/UserManagement.Web).");
        }

        return webProject;
    }

    /// <summary>Saves a screenshot into the artifact directory and returns its path.</summary>
    public async Task<string> ScreenshotAsync(IPage page, string name)
    {
        var path = Path.Combine(ArtifactDirectory, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        return path;
    }

    /// <summary>
    /// Builds an absolute URL for the Web app. Playwright requires absolute URLs.
    /// </summary>
    public string Url(string relativePath)
        => relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? relativePath
            : WebBaseAddress.ToString().TrimEnd('/') + relativePath;

    private static async Task<IHost> BuildApiHostAsync(IConfiguration config)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddConfiguration(config);
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

        builder.WebHost.ConfigureKestrel(opts => { opts.Limits.MaxRequestBodySize = maxRequestBodyBytes; });

        var app = builder.Build();
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
        app.MapGet("/health", () => Microsoft.AspNetCore.Http.Results.Ok(new { status = "ok" }));

        return app;
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _browser = null;
        _playwright?.Dispose();
        _playwright = null;

        if (_webHost is not null)
        {
            await _webHost.StopAsync();
            _webHost.Dispose();
        }

        if (_apiHost is not null)
        {
            await _apiHost.StopAsync();
            _apiHost.Dispose();
        }

        await DropDatabaseAsync();

        try
        {
            if (Directory.Exists(FileStorageRoot)) Directory.Delete(FileStorageRoot, recursive: true);
        }
        catch { /* ignore */ }

        Environment.SetEnvironmentVariable("Api__BaseUrl", null);
        Environment.SetEnvironmentVariable("Api__TimeoutSeconds", null);
        Environment.SetEnvironmentVariable("Display__TimeZoneId", null);
        Environment.SetEnvironmentVariable("DISABLE_HTTPS_REDIRECT", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTPS_PORT", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("Api__BaseUrl", null);
        Environment.SetEnvironmentVariable("Api__TimeoutSeconds", null);
        Environment.SetEnvironmentVariable("Display__TimeZoneId", null);
        Environment.SetEnvironmentVariable("DISABLE_HTTPS_REDIRECT", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTPS_PORT", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);
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
