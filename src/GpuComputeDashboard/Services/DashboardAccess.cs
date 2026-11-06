using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace GpuComputeDashboard.Services;

internal sealed class DashboardAccess
{
    private readonly bool allowUnauthenticatedLan;
    private readonly byte[]? credentialHash;
    private readonly System.Net.IPNetwork? allowedNetwork;

    public DashboardAccess(IConfigurationSection settings)
    {
        var authenticationEnabled = settings.GetValue<bool>("Enabled");
        allowUnauthenticatedLan = settings.GetValue<bool>("AllowUnauthenticatedLan");
        if (authenticationEnabled || allowUnauthenticatedLan)
        {
            if (settings["AllowedNetwork"] is { } network)
                allowedNetwork = System.Net.IPNetwork.Parse(network);
            if (allowUnauthenticatedLan && allowedNetwork is not { PrefixLength: > 0 })
                throw new InvalidOperationException("Unauthenticated LAN access requires a restricted AllowedNetwork CIDR.");
        }

        if (!authenticationEnabled)
            return;

        var username = settings["Username"];
        var password = settings["Password"];
        if (string.IsNullOrWhiteSpace(username) || username.Contains(':') || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Dashboard access requires a username and password.");

        credentialHash = SHA256.HashData(Encoding.UTF8.GetBytes(username + ":" + password));
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote?.IsIPv4MappedToIPv6 == true)
            remote = remote.MapToIPv4();

        if (credentialHash is null && !allowUnauthenticatedLan)
        {
            if (remote is not null && IPAddress.IsLoopback(remote))
                await next(context);
            else
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!context.Request.IsHttps ||
            (allowedNetwork is { } network && (remote is null || !network.Contains(remote))))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Browsers attach cached Basic credentials to requests, including WebSocket upgrades.
        // Only allow browser requests that specify an origin when that origin is this dashboard.
        if (context.Request.Headers.TryGetValue("Origin", out var origin) &&
            !string.Equals(origin, "https://" + context.Request.Host, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (credentialHash is not null && !MatchesCredentials(context.Request.Headers.Authorization))
        {
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"VectorGPU\", charset=\"UTF-8\"";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    private bool MatchesCredentials(string? authorization)
    {
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !header.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase) ||
            header.Parameter is null)
            return false;

        try
        {
            var suppliedHash = SHA256.HashData(Convert.FromBase64String(header.Parameter));
            return CryptographicOperations.FixedTimeEquals(suppliedHash, credentialHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
