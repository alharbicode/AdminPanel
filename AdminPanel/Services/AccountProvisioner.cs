using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Services;

public static class AccountProvisioner
{
    // Explicit operator command only. Password is read from configuration, never an argument or log.
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, bool reset)
    {
        var username = configuration["BootstrapAdmin:Username"];
        var password = configuration["BootstrapAdmin:Password"];
        var email = configuration["BootstrapAdmin:Email"];
        if (string.IsNullOrWhiteSpace(username) || username.Length > 256 ||
            string.IsNullOrWhiteSpace(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email) ||
            string.IsNullOrWhiteSpace(password) || password.Length < 12 || password.Length > 1024)
            throw new InvalidOperationException("Set BootstrapAdmin:Username, Email and Password (at least 12 characters).");

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var matches = await db.Users.Where(u => u.Username == username).Take(2).ToListAsync();
        if (matches.Count > 1) throw new InvalidOperationException("Resolve duplicate usernames before provisioning.");
        var user = matches.SingleOrDefault();
        if (reset && user is null) throw new InvalidOperationException("The account to reset does not exist.");
        if (!reset && user is not null) throw new InvalidOperationException("The account already exists. Use --reset-password explicitly.");
        if (user is null)
        {
            user = new User { Username = username, Email = email, Role = "Admin" };
            db.Users.Add(user);
        }
        user.Password = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, password);
        await db.SaveChangesAsync();
    }
}

