using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using UserManagement.Api.Data;
using UserManagement.Api.FileStorage;
using UserManagement.Api.Middleware;
using UserManagement.Api.Ocr;
using UserManagement.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// =================== Services ===================

builder.Services.AddControllers(options =>
    {
        // Convert ModelState errors to a single ValidationException with camelCase keys.
        options.Filters.AddService<ModelStateValidationFilter>();
    })
    .AddJsonOptions(options =>
    {
        // Force camelCase property names (per API-Spec §1.1) so error keys line up with
        // JSON property names. Also accept enum values as their string name (e.g. "Male").
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(
                System.Text.Json.JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Use our own validation pipeline so we can produce camelCase field names and
        // a single, consistent error format.
        options.SuppressModelStateInvalidFilter = true;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "User Management API",
        Version = "v1",
        Description = "REST API for User Management (CRUD Users, Identity Documents, Image storage)."
    });
});

// ProblemDetails (RFC 7807 / 9457). Customize to include traceId.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var http = context.HttpContext;
        var traceId = http.TraceIdentifier;
        if (!string.IsNullOrEmpty(traceId))
        {
            context.ProblemDetails.Extensions["traceId"] = traceId;
        }
    };
});

// CORS - chỉ cho phép origin Frontend
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// FileStorage options (kept for the image validation limits: max size, max pixels).
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

// Cloudinary storage - replaces the local file storage for identity document images.
builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection(CloudinaryOptions.SectionName));
builder.Services.AddHttpClient<ICloudinaryStorage, CloudinaryStorage>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CloudinaryOptions>>().Value;
    client.Timeout = options.Timeout;
    client.BaseAddress = new Uri("https://api.cloudinary.com");
});

// OCR options + PaddleOCR HTTP client.
builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection(OcrOptions.SectionName));
builder.Services.AddHttpClient<IPaddleOcrClient, PaddleOcrClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OcrOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds <= 0 ? 30 : options.TimeoutSeconds);
});

// EF Core / SQL Server
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

// Domain services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IIdentityDocumentService, IdentityDocumentService>();
builder.Services.AddScoped<IOcrService, OcrService>();
builder.Services.AddSingleton<ImageValidator>();
builder.Services.AddSingleton<OcrNormalizer>();

// Background job that removes the temporary OCR images after they expire (BR-15).
builder.Services.AddHostedService<OcrCleanupService>();

// Kestrel / form options for large file uploads
var maxRequestBodyBytes = builder.Configuration.GetValue<long?>("FileStorage:MaxRequestBodyBytes") ?? 11_534_336L;
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodyBytes;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxRequestBodyBytes;
    options.ValueLengthLimit = int.MaxValue;
});

builder.Services.AddHttpContextAccessor();

// Global exception handler (RFC 7807 ProblemDetails, no stack trace leak).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// MVC filter: convert ModelState errors to a single ValidationException with camelCase field names.
builder.Services.AddScoped<ModelStateValidationFilter>();

// =================== Pipeline ===================

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "User Management API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();
app.UseAuthorization();

app.MapControllers();

// Health endpoint for liveness checks
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .AllowAnonymous();

app.Run();

// Make Program visible to integration tests.
namespace UserManagement.Api
{
    public partial class Program { }
}
