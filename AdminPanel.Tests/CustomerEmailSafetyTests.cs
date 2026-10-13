using AdminPanel.API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace AdminPanel.Tests;

public sealed class CustomerEmailSafetyTests
{
    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "DevelopmentFile")]
    [InlineData("Staging", "DevelopmentFile")]
    public async Task Nondevelopment_never_uses_the_private_development_outbox(string environment, string? provider)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CustomerEmail:Provider"] = provider
        }).Build();
        var sender = new PasswordResetEmail(config, new EmailEnvironment { EnvironmentName = environment });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(
            "fixture@example.test", CustomerSecurity.NewSecret(), "en", CancellationToken.None));
    }

    [Fact]
    public async Task Recovery_links_require_a_trusted_https_origin()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CustomerEmail:StorefrontUrl"] = "http://localhost:7289"
        }).Build();
        var sender = new PasswordResetEmail(config, new EmailEnvironment { EnvironmentName = "Development" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(
            "fixture@example.test", CustomerSecurity.NewSecret(), "ar", CancellationToken.None));
    }

    private sealed class EmailEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "EmailSafetyTests";
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
