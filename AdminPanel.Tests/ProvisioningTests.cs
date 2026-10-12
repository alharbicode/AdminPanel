using AdminPanel.API.Data;
using AdminPanel.API.Models;
using AdminPanel.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminPanel.Tests;

public sealed class ProvisioningTests
{
    [Fact]
    public async Task Provision_and_reset_hash_passwords_without_overwriting_permissions()
    {
        using var api = new ApiFactory();
        api.Seed();
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Username"] = "operator",
            ["BootstrapAdmin:Email"] = "operator@example.test",
            ["BootstrapAdmin:Password"] = password
        }).Build();
        await AccountProvisioner.RunAsync(api.Services, config, reset: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AccountProvisioner.RunAsync(api.Services, config, reset: false));
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Username == "operator");
        Assert.Equal("Admin", user.Role);
        Assert.NotEqual(password, user.Password);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<User>().VerifyHashedPassword(user, user.Password, password));
        user.Role = "Staff";
        user.IsBlocked = true;
        await db.SaveChangesAsync();
        config["BootstrapAdmin:Password"] = password + "new";
        await AccountProvisioner.RunAsync(api.Services, config, reset: true);
        await db.Entry(user).ReloadAsync();
        Assert.True(user.IsBlocked);
        Assert.Equal("Staff", user.Role);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<User>().VerifyHashedPassword(user, user.Password, password + "new"));
    }
}
