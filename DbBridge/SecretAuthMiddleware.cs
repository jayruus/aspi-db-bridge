namespace DbBridge;

using Microsoft.AspNetCore.Http;
using System.Net;
using System.Threading.Tasks;

public class SecretAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _bridgeSecret;

    public SecretAuthMiddleware(RequestDelegate next)
    {
        _next = next;
        _bridgeSecret = Environment.GetEnvironmentVariable("BRIDGE_SECRET") ?? "dev-bridge-secret-2025";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check secret header
        if (!string.IsNullOrEmpty(_bridgeSecret))
        {
            var secretHeader = context.Request.Headers["X-Bridge-Secret"].FirstOrDefault();
            if (secretHeader == _bridgeSecret)
            {
                await _next(context);
                return;
            }
        }

        // Deny
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"ok\": false, \"error\": \"AUTH_FAILED\"}");
    }
}