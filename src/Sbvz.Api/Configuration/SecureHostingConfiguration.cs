using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Sbvz.Api.Configuration;

internal static class SecureHostingConfiguration
{
    private const string AllowedHostsVariable = "SBVZ_ALLOWED_HOSTS";
    private const string TrustedProxiesVariable = "SBVZ_TRUSTED_PROXIES";

    public static void Configure(
        WebApplicationBuilder builder)
    {
        builder.Services.AddHostFiltering(
            options => options.AllowedHosts = ReadAllowedHosts(
                builder.Configuration,
                builder.Environment));
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            var configuredProxies = ParseTrustedProxies(
                builder.Configuration[TrustedProxiesVariable]);

            if (configuredProxies.Count == 0)
            {
                return;
            }

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var proxy in configuredProxies)
            {
                options.KnownProxies.Add(proxy);
            }
        });
    }

    private static string[] ReadAllowedHosts(
        ConfigurationManager configuration,
        IWebHostEnvironment environment)
    {
        var configured = configuration[AllowedHostsVariable];

        if (string.IsNullOrWhiteSpace(configured))
        {
            if (environment.IsDevelopment())
            {
                return ["localhost", "127.0.0.1"];
            }

            throw new InvalidOperationException(
                $"{AllowedHostsVariable} must contain one or more explicit hostnames outside Development.");
        }

        var hosts = configured
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (hosts.Length == 0 || hosts.Any(host => !IsValidHost(host)))
        {
            throw new InvalidOperationException(
                $"{AllowedHostsVariable} must contain semicolon-separated hostnames without schemes, paths, ports or wildcards.");
        }

        return hosts;
    }

    private static List<IPAddress> ParseTrustedProxies(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return [];
        }

        var proxies = new List<IPAddress>();

        foreach (var value in configured.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPAddress.TryParse(value, out var address))
            {
                throw new InvalidOperationException(
                    $"{TrustedProxiesVariable} must contain comma-separated IP addresses.");
            }

            proxies.Add(address);
        }

        return proxies;
    }

    private static bool IsValidHost(string host)
    {
        return host.Length <= 253
            && !host.Contains('*', StringComparison.Ordinal)
            && !host.Contains(':', StringComparison.Ordinal)
            && Uri.CheckHostName(host) is not UriHostNameType.Unknown;
    }
}
