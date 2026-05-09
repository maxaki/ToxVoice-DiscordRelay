using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using ToxVoice.DiscordRelay.Configuration;
using ToxVoice.DiscordRelay.Forwarding;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(AppContext.BaseDirectory, "logs", "toxvoice-relay-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Host.UseSerilog();

builder.Services
    .AddOptions<RelayOptions>()
    .Bind(builder.Configuration.GetSection(RelayOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<OutboundHttpClientPool>();
builder.Services.AddSingleton<DiscordForwarder>();

var relayOptions = builder.Configuration.GetSection(RelayOptions.SectionName).Get<RelayOptions>() ?? new RelayOptions();

builder.WebHost.ConfigureKestrel(options =>
{
    if (System.Net.IPAddress.TryParse(relayOptions.BindAddress, out var bindAddress))
    {
        options.Listen(bindAddress, relayOptions.Port);
    }
    else
    {
        Log.Warning(
            "Invalid BindAddress '{BindAddress}', falling back to 127.0.0.1.", relayOptions.BindAddress);
        options.ListenLocalhost(relayOptions.Port);
    }
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/webhook/{name}", async (
    string name,
    HttpContext httpContext,
    DiscordForwarder forwarder,
    IOptions<RelayOptions> options,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (!options.Value.Webhooks.TryGetValue(name, out var targetUrl) || string.IsNullOrWhiteSpace(targetUrl))
    {
        logger.LogWarning("Unknown webhook name requested: {Name}", name);
        return Results.NotFound(new { error = $"Unknown webhook: {name}" });
    }

    if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var targetUri))
    {
        logger.LogError("Invalid Discord webhook URL configured for {Name}: {Url}", name, targetUrl);
        return Results.Problem("Misconfigured webhook target.", statusCode: 500);
    }

    using var bodyStream = new MemoryStream();
    await httpContext.Request.Body.CopyToAsync(bodyStream, cancellationToken).ConfigureAwait(false);
    var body = bodyStream.ToArray();
    var contentType = httpContext.Request.ContentType ?? "application/octet-stream";

    var result = await forwarder.ForwardAsync(targetUri, body, contentType, cancellationToken).ConfigureAwait(false);

    httpContext.Response.StatusCode = (int)result.StatusCode;
    httpContext.Response.ContentType = result.ContentType;
    httpContext.Response.Headers["X-Relay-Outbound-Ip"] = result.OutboundIp;
    httpContext.Response.Headers["X-Relay-Attempts"] = result.Attempts.ToString();

    if (result.Body.Length > 0)
        await httpContext.Response.Body.WriteAsync(result.Body, cancellationToken).ConfigureAwait(false);

    return Results.Empty;
});

using (var scope = app.Services.CreateScope())
{
    var validatedOptions = scope.ServiceProvider.GetRequiredService<IOptions<RelayOptions>>().Value;
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (validatedOptions.Webhooks.Count == 0)
    {
        startupLogger.LogWarning(
            "No webhooks configured. Add entries under Relay:Webhooks in appsettings.json before forwarding.");
    }
    else
    {
        startupLogger.LogInformation(
            "Configured webhooks: [{Names}]", string.Join(", ", validatedOptions.Webhooks.Keys));
    }

    startupLogger.LogInformation(
        "Listening on http://{Address}:{Port}", validatedOptions.BindAddress, validatedOptions.Port);
}

try
{
    await app.RunAsync().ConfigureAwait(false);
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}
