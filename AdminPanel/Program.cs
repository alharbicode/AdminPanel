using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using AdminPanel.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args.Where(a => a is not ("--bootstrap-admin" or "--reset-password")).ToArray());
var secret = builder.Configuration["JwtSettings:SecretKey"];
if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
    throw new InvalidOperationException("Configure JwtSettings:SecretKey with a random secret of at least 32 bytes.");
var issuer = builder.Configuration["JwtSettings:Issuer"];
var audience = builder.Configuration["JwtSettings:Audience"];
if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
    throw new InvalidOperationException("Configure JwtSettings:Issuer and Audience.");
if (builder.Configuration.GetValue<int>("JwtSettings:LifetimeMinutes", 60) is < 1 or > 1440)
    throw new InvalidOperationException("JwtSettings:LifetimeMinutes must be between 1 and 1440.");
var connection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection for SQL Server.");

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<ProductImageStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CatalogVisibility>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<IPasswordResetEmail, PasswordResetEmail>();
builder.Services.AddAuthentication().AddJwtBearer(CustomerSecurity.Scheme, options => CustomerSecurity.Configure(options, builder.Configuration));
if (audience == CustomerSecurity.Audience)
    throw new InvalidOperationException("Administrator and customer token audiences must be distinct.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = issuer,
        ValidateAudience = true, ValidAudience = audience,
        ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
        ValidateIssuerSigningKey = true,
        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var user = int.TryParse(id, out var userId) ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId) : null;
            if (user is null || user.IsBlocked || user.Role != context.Principal?.FindFirstValue(ClaimTypes.Role))
                context.Fail("Account access is no longer valid.");
        }
    };
});
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole("Admin").Build());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("customer-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("guest-cart", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();

if (args.Contains("--bootstrap-admin") || args.Contains("--reset-password"))
{
    await AccountProvisioner.RunAsync(app.Services, app.Configuration, args.Contains("--reset-password"));
    return;
}
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (CartProblem problem)
    {
        context.Response.StatusCode = problem.Status;
        await context.Response.WriteAsJsonAsync(new { code = problem.Code });
    }
    catch (DbUpdateConcurrencyException)
    {
        await Results.Problem(statusCode: 409, title: "Data changed. Reload and retry.").ExecuteAsync(context);
    }
    catch (DbUpdateException)
    {
        await Results.Problem(statusCode: 409, title: "The change conflicts with related data.").ExecuteAsync(context);
    }
});
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.Run();

public partial class Program { }
