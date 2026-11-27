using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
/// Harness for the LIVE OCR browser test.
///
/// Unlike <see cref="BrowserTestHarness"/>, this one does NOT substitute
/// <c>FakePaddleOcrClient</c> - it registers the real <see cref="PaddleOcrClient"/>
/// so the browser drives the genuine Web -> API -> PaddleOCR pipeline. The PaddleOCR
/// microservice must be running on http://localhost:8100 for the test to pass.
///
/// The test is skipped (not failed) when the service is unreachable, because the
/// microservice is an optional local dependency.
/// </summary>
public class LiveOcrTestHarness : IAsyncLifetime, IDisposable
{
    public string DatabaseName { get; } = $"UserManagementDb_LiveOcr_{Guid.NewGuid():N}";
    public string FileStorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"UserManagement_LiveOcr_{Guid.NewGuid():N}");

    private IHost? _apiHost;
    private IHost? _webHost;
    private int _apiPort;
    private int _webPort;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public Uri WebBaseAddress => new($"http://localhost:{_webPort}");
    public Uri ApiBaseAddress => new($"http://localhost:{_apiPort}");

    public IPlaywright PlaywrightInstance => _playwright
        ?? throw new InvalidOperationException("Playwright has not been started yet.");

    public IBrowser Browser => _browser
        ?? throw new InvalidOperationException("The browser has not been started yet.");

    /// <summary>True when the PaddleOCR microservice answers on /health.</summary>
    public static async Task<bool> IsOcrServiceAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync("http://localhost:8100/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(FileStorageRoot);

        _apiPort = FindFreePort();
        _webPort = FindFreePort();

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
                ["Ocr:TimeoutSeconds"] = "120",
                ["Ocr:MinConfidence"] = "0.60",
                ["Ocr:HighConfidence"] = "0.85",
                ["Logging:LogLevel:Default"] = "Warning",
                ["urls"] = $"http://localhost:{_apiPort}"
            })
            .Build();

        _apiHost = await BuildApiHostAsync(apiConfig, _apiPort);
        await _apiHost.StartAsync();

        using (var scope = _apiHost.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
        }

        Environment.SetEnvironmentVariable("Api__BaseUrl", ApiBaseAddress.ToString().TrimEnd('/'));
        Environment.SetEnvironmentVariable("Api__TimeoutSeconds", "120");
        Environment.SetEnvironmentVariable("Display__TimeZoneId", "SE Asia Standard Time");
        Environment.SetEnvironmentVariable("DISABLE_HTTPS_REDIRECT", "true");
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTPS_PORT", "");

        var webRoot = ResolveWebRoot();

        var webBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = webRoot,
            WebRootPath = Path.Combine(webRoot, "wwwroot")
        });
        webBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:BaseUrl"] = ApiBaseAddress.ToString().TrimEnd('/'),
            ["Api:TimeoutSeconds"] = "120",
            ["Display:TimeZoneId"] = "SE Asia Standard Time"
        });
        webBuilder.Environment.EnvironmentName = "Development";
        webBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        webBuilder.WebHost.UseUrls($"http://localhost:{_webPort}");

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
            client.Timeout = TimeSpan.FromSeconds(120);
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

        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

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

    private static async Task<IHost> BuildApiHostAsync(IConfiguration config, int apiPort)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddConfiguration(config);
        var maxRequestBodyBytes = builder.Configuration.GetValue<long?>("FileStorage:MaxRequestBodyBytes") ?? 11_534_336L;

        builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));
        builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
        builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection(CloudinaryOptions.SectionName));
        builder.Services.AddSingleton<ICloudinaryStorage, TestCloudinaryStorage>();
        builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection(OcrOptions.SectionName));

        // The REAL OCR client - the whole point of this harness.
        builder.Services.AddHttpClient<IPaddleOcrClient, PaddleOcrClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OcrOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds <= 0 ? 30 : options.TimeoutSeconds);
        });
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

        builder.Services.AddEndpointsApiExplorer();
        builder.WebHost.ConfigureKestrel(opts => { opts.Limits.MaxRequestBodySize = maxRequestBodyBytes; });

        // Bind explicitly to the API port. The ASPNETCORE_URLS environment variable is
        // shared with the Web host, so relying on it would make both hosts fight over
        // the same port.
        builder.WebHost.UseUrls($"http://localhost:{apiPort}");

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

    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string ResolveWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "SRC", "UserManagement.Web");
            if (Directory.Exists(Path.Combine(candidate, "wwwroot")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate SRC/UserManagement.Web from " + AppContext.BaseDirectory);
    }

    public async Task DisposeAsync()
    {
        if (_webHost is not null) await _webHost.StopAsync();
        if (_apiHost is not null) await _apiHost.StopAsync();

        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();

        _webHost?.Dispose();
        _apiHost?.Dispose();

        try
        {
            if (Directory.Exists(FileStorageRoot)) Directory.Delete(FileStorageRoot, true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    public void Dispose()
    {
    }
}
