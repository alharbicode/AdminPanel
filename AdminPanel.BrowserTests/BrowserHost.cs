using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AdminPanel.Tests;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace AdminPanel.BrowserTests;

// Loopback HTTPS transport into the real TestServer pipelines. No mocked controllers or API responses.
// The existing fixture enforces disposable SQLite / loopback SQL Server databases and random credentials.
internal sealed class BrowserHost : IAsyncDisposable
{
    public ApiFactory Api { get; } = new();
    private MvcFactory? _mvc;
    private HttpClient? _pipeline;
    private WebApplication? _bridge;
    private X509Certificate2? _certificate;
    public string Url { get; private set; } = "";

    public async Task Start()
    {
        Api.Seed();
        _mvc = new MvcFactory(Api);
        // Initialize the factory before obtaining its handler. Do not share a client cookie container.
        _ = _mvc.Services;
        _pipeline = new HttpClient(_mvc.Server.CreateHandler());
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        _certificate = new X509Certificate2(temporary.Export(X509ContentType.Pfx));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(_certificate)));
        _bridge = builder.Build();
        _bridge.Run(async context =>
        {
            using var upstream = new HttpRequestMessage(new HttpMethod(context.Request.Method),
                "https://localhost" + context.Request.PathBase + context.Request.Path + context.Request.QueryString);
            if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
                upstream.Content = new StreamContent(context.Request.Body);
            foreach (var header in context.Request.Headers)
            {
                if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                if (!upstream.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    upstream.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            using var response = await _pipeline.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers) context.Response.Headers[header.Key] = header.Value.ToArray();
            foreach (var header in response.Content.Headers) context.Response.Headers[header.Key] = header.Value.ToArray();
            context.Response.Headers.Remove("transfer-encoding");
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        await _bridge.StartAsync();
        Url = _bridge.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    public async ValueTask DisposeAsync()
    {
        if (_bridge is not null) { await _bridge.StopAsync(); await _bridge.DisposeAsync(); }
        _pipeline?.Dispose();
        _mvc?.Dispose();
        Api.Dispose();
        _certificate?.Dispose();
    }
}
