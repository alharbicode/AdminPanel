using System.Security.Claims;
using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using AdminPanel.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace AdminPanel.API.Controllers;

[ApiController, Route("api/customer/account")]
[Authorize(AuthenticationSchemes = CustomerSecurity.Scheme, Roles = "Customer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerAccountsController(ApplicationDbContext db, IPasswordHasher<User> hasher,
    IConfiguration config, TimeProvider clock, IPasswordResetEmail email, ILogger<CustomerAccountsController> logger) : ControllerBase
{
    private static readonly User Dummy = new();
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(Dummy, CustomerSecurity.NewSecret());
    private int CustomerId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static CustomerView View(CustomerAccount a) => new(a.UserId, a.DisplayName, a.User.Email);
    private CustomerLoginResult LoginResult(CustomerAccount a) => new(CustomerSecurity.Token(a, config, clock),
        CustomerSecurity.LifetimeSeconds, View(a));
    private bool PasswordMatches(User? user, string password)
    {
        try { return hasher.VerifyHashedPassword(user ?? Dummy, user?.Password ?? DummyHash, password) != PasswordVerificationResult.Failed; }
        catch (FormatException) { return false; }
    }
    private Task<CustomerAccount> Own(CancellationToken ct) => db.CustomerAccounts.Include(a => a.User).SingleAsync(a => a.UserId == CustomerId, ct);

    [AllowAnonymous, HttpPost("register"), EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Register(RegisterCustomerDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.DisplayName)) return BadRequest(new { code = "InvalidInput" });
        var normalized = CustomerSecurity.Email(dto.Email);
        if (await db.CustomerAccounts.AnyAsync(a => a.NormalizedEmail == normalized, ct))
            return Conflict(new { code = "EmailInUse" });
        var user = new User { Username = "customer-" + Guid.NewGuid().ToString("N"), Email = dto.Email.Trim(), Role = "Customer" };
        user.Password = hasher.HashPassword(user, dto.Password);
        var account = new CustomerAccount { User = user, NormalizedEmail = normalized, DisplayName = dto.DisplayName.Trim() };
        db.CustomerAccounts.Add(account);
        db.ShoppingCarts.Add(new ShoppingCart { Customer = account });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { code = "EmailInUse" }); }
        return Ok(LoginResult(account));
    }

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Login(CustomerLoginDto dto, CancellationToken ct)
    {
        var normalized = CustomerSecurity.Email(dto.Email);
        var account = await db.CustomerAccounts.Include(a => a.User).SingleOrDefaultAsync(a => a.NormalizedEmail == normalized, ct);
        var valid = PasswordMatches(account?.User, dto.Password);
        if (account is null || account.User.IsBlocked || account.User.Role != "Customer" ||
            account.LockedUntilUtcTicks > clock.GetUtcNow().UtcTicks)
            return Unauthorized(new { code = "InvalidCredentials" });
        if (!valid)
        {
            account.FailedLogins = Math.Min(account.FailedLogins + 1, 10);
            if (account.FailedLogins >= 10) { account.LockedUntilUtcTicks = clock.GetUtcNow().AddMinutes(15).UtcTicks; account.FailedLogins = 0; }
            account.Version = Guid.NewGuid();
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { }
            return Unauthorized(new { code = "InvalidCredentials" });
        }
        account.FailedLogins = 0; account.LockedUntilUtcTicks = null; account.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return Ok(LoginResult(account));
    }

    [AllowAnonymous, HttpPost("forgot-password"), EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Forgot(ForgotPasswordDto dto, CancellationToken ct)
    {
        var normalized = CustomerSecurity.Email(dto.Email);
        var account = await db.CustomerAccounts.Include(a => a.User).SingleOrDefaultAsync(a => a.NormalizedEmail == normalized, ct);
        if (account is not null && !account.User.IsBlocked && account.User.Role == "Customer")
        {
            var token = CustomerSecurity.NewSecret();
            account.ResetTokenHash = CustomerSecurity.Hash(token);
            account.ResetExpiresUtcTicks = clock.GetUtcNow().AddMinutes(30).UtcTicks;
            account.Version = Guid.NewGuid();
            try
            {
                await db.SaveChangesAsync(ct);
                await email.Send(account.User.Email, token, dto.Culture, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Do not log exception bodies, email addresses, reset links or tokens.
                logger.LogWarning("Customer recovery delivery did not complete. Check private email-provider configuration.");
            }
        }
        return Accepted(new { code = "RecoveryRequested" });
    }

    [AllowAnonymous, HttpPost("reset-password"), EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Reset(ResetPasswordDto dto, CancellationToken ct)
    {
        var normalized = CustomerSecurity.Email(dto.Email);
        var hash = CustomerSecurity.Hash(dto.Token);
        var now = clock.GetUtcNow().UtcTicks;
        var account = await db.CustomerAccounts.Include(a => a.User)
            .SingleOrDefaultAsync(a => a.NormalizedEmail == normalized && a.ResetTokenHash == hash && a.ResetExpiresUtcTicks > now, ct);
        if (account is null || account.User.IsBlocked || account.User.Role != "Customer")
            return BadRequest(new { code = "ResetInvalid" });
        SetPassword(account, dto.Password);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return BadRequest(new { code = "ResetInvalid" }); }
        return Ok(new { code = "PasswordUpdated" });
    }
    private void SetPassword(CustomerAccount account, string password)
    {
        account.User.Password = hasher.HashPassword(account.User, password);
        account.SecurityStamp = Guid.NewGuid(); account.Version = Guid.NewGuid();
        account.ResetTokenHash = null; account.ResetExpiresUtcTicks = null;
        account.FailedLogins = 0; account.LockedUntilUtcTicks = null;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile(CancellationToken ct) => Ok(View(await Own(ct)));

    [HttpPut("profile")]
    public async Task<IActionResult> Profile(CustomerProfileDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.DisplayName)) return BadRequest(new { code = "InvalidInput" });
        var account = await Own(ct);
        account.DisplayName = dto.DisplayName.Trim(); account.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return Ok(View(account));
    }

    [HttpPost("change-password"), EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> ChangePassword(ChangeCustomerPasswordDto dto, CancellationToken ct)
    {
        var account = await Own(ct);
        if (!PasswordMatches(account.User, dto.CurrentPassword)) return BadRequest(new { code = "InvalidCredentials" });
        SetPassword(account, dto.Password);
        await db.SaveChangesAsync(ct);
        return Ok(new { code = "PasswordUpdated" });
    }
}

