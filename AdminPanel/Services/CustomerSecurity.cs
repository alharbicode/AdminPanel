using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
namespace AdminPanel.API.Services;

public static class CustomerSecurity
{
    public const string Scheme = "CustomerBearer";
    public const string Audience = "AdminPanel.Storefront.Customer";
    public const int LifetimeSeconds = 3600;
    public static string NewSecret() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    public static string Email(string email) => email.Trim().ToUpperInvariant();
    public static void Configure(JwtBearerOptions options, IConfiguration config)
    {
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true, ValidIssuer = config["JwtSettings:Issuer"],
            ValidateAudience = true, ValidAudience = Audience,
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero, ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JwtSettings:SecretKey"]!))
        };
        options.Events = new()
        {
            OnTokenValidated = async context =>
            {
                var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var stamp = context.Principal?.FindFirstValue("customer_stamp");
                var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                var account = int.TryParse(id, out var userId)
                    ? await db.CustomerAccounts.AsNoTracking().Include(a => a.User).SingleOrDefaultAsync(a => a.UserId == userId)
                    : null;
                if (account is null || account.User.IsBlocked || account.User.Role != "Customer" ||
                    context.Principal?.FindFirstValue(ClaimTypes.Role) != "Customer" || account.SecurityStamp.ToString("N") != stamp)
                    context.Fail("Customer session is no longer valid.");
            }
        };
    }
    public static string Token(CustomerAccount account, IConfiguration config, TimeProvider clock)
    {
        var jwt = new JwtSecurityToken(issuer: config["JwtSettings:Issuer"], audience: Audience,
            claims: [new(ClaimTypes.NameIdentifier, account.UserId.ToString()), new(ClaimTypes.Role, "Customer"),
                new("customer_stamp", account.SecurityStamp.ToString("N"))],
            expires: clock.GetUtcNow().AddSeconds(LifetimeSeconds).UtcDateTime,
            signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JwtSettings:SecretKey"]!)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}

