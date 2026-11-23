using UserManagement.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// =================== Services ===================

builder.Services.AddControllersWithViews();

// Anti-forgery for POST/DELETE controllers (UI-Spec §1.4 + §7.2 require it for delete actions).
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "UserManagement.Antiforgery";
});

// Typed HttpClient for the Backend API. Base URL comes from configuration (Api:BaseUrl).
var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Api:BaseUrl must be configured.");
var apiTimeoutSeconds = builder.Configuration.GetValue<int?>("Api:TimeoutSeconds") ?? 30;

builder.Services.AddHttpClient<IApiClient, ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(apiTimeoutSeconds);
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddScoped<IUsersApiClient, UsersApiClient>();

// Time zone configuration for display layer (Vietnam).
builder.Services.Configure<DisplayOptions>(builder.Configuration.GetSection("Display"));

// =================== Pipeline ===================

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

var enableHttpsRedirect = !string.Equals(
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
    "Testing",
    StringComparison.OrdinalIgnoreCase)
    && !string.Equals(
        Environment.GetEnvironmentVariable("DISABLE_HTTPS_REDIRECT"),
        "true",
        StringComparison.OrdinalIgnoreCase);

if (enableHttpsRedirect)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Auto-redirect "/" -> "/Users" per UI-Spec §2.
app.MapGet("/", context =>
{
    context.Response.Redirect("/Users");
    return Task.CompletedTask;
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Users}/{action=Index}/{id?}");

app.Run();

namespace UserManagement.Web
{
    public partial class Program { }
}
