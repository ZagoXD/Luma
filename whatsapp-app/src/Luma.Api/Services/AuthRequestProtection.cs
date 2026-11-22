namespace Luma.Api.Services;

public sealed class AuthRequestProtection(RequestDelegate next, IWebHostEnvironment environment, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context, IAccountRequestLimiter limiter)
    {
        var path = context.Request.Path.Value ?? "";
        var developmentKey = configuration["Development:AccessToken"] ?? "";
        if (context.Request.Path.StartsWithSegments("/dev") && (!environment.IsDevelopment()
            || context.Connection.RemoteIpAddress is not { } address || !System.Net.IPAddress.IsLoopback(address)
            || developmentKey.Length < 32 || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(developmentKey)),
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(context.Request.Headers["X-Luma-Development-Key"].ToString())))))
        {
            context.Response.StatusCode = 404;
            return;
        }
        var auth = path is "/account/register" or "/account/login" or "/account/refresh" or "/account/logout"
            or "/auth/forgot-password" or "/auth/reset-password" || path.StartsWith("/account/phone-", StringComparison.Ordinal);
        try
        {
            if (auth && HttpMethods.IsPost(context.Request.Method))
            {
                if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
                    size.MaxRequestBodySize = 8192;
                if (context.Request.ContentLength > 8192) { context.Response.StatusCode = 413; return; }
                // The API never trusts a caller-supplied forwarding header for abuse protection.
                var network = context.Connection.RemoteIpAddress?.ToString() ?? "internal";
                if (!await limiter.AllowAsync("auth.network:" + path, network, 300, TimeSpan.FromMinutes(5), context.RequestAborted))
                    throw new AuthRateLimitException(300);
            }
            await next(context);
        }
        catch (AuthRateLimitException error) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 429;
            context.Response.Headers.RetryAfter = error.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { message = error.Message }, context.RequestAborted);
        }
    }
}
