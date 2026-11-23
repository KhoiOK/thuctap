using System.Net.Http.Json;

namespace UserManagement.Web.Services;

/// <summary>
/// Marker interface for the Backend API client. Concrete typed client is implemented
/// in Phase 5 (Frontend MVC and API client).
/// </summary>
public interface IApiClient
{
    HttpClient HttpClient { get; }
}

/// <summary>
/// Default <see cref="IApiClient"/> implementation - thin wrapper around an HttpClient
/// configured via <see cref="IHttpClientFactory"/>. Concrete endpoints will be added in Phase 5.
/// </summary>
public class ApiClient : IApiClient
{
    public ApiClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }
}
