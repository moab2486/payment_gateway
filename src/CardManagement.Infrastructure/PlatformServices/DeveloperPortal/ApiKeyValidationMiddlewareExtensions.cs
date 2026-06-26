using Microsoft.AspNetCore.Builder;

namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal;

/// <summary>
/// Extension methods for registering the API key validation middleware in the ASP.NET Core pipeline.
/// </summary>
public static class ApiKeyValidationMiddlewareExtensions
{
    /// <summary>
    /// Adds the API key validation middleware to the request pipeline.
    /// This middleware validates the X-Api-Key header, checks Redis cache first
    /// with a DB fallback, and attaches developer context to HttpContext.Items.
    /// 
    /// Should be registered before the request logging middleware and controller routing.
    /// </summary>
    public static IApplicationBuilder UseApiKeyValidation(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ApiKeyValidationMiddleware>();
    }
}
