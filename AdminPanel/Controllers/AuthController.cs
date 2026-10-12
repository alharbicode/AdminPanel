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

[Route("api/[controller]")]
[ApiController]
public class AuthController(ApplicationDbContext context, JwtTokenService tokens, IPasswordHasher<User> hasher) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        // Duplicate legacy usernames are not allowed to select an arbitrary account.
        var matches = await context.Users.Where(u => u.Username == dto.Username).Take(2).ToListAsync();
        var user = matches.Count == 1 ? matches[0] : null;
        var result = PasswordVerificationResult.Failed;
        if (user is not null)
        {
            try { result = hasher.VerifyHashedPassword(user, user.Password, dto.Password); }
            catch (FormatException) { /* Legacy plaintext requires an explicit password reset. */ }
        }
        if (user is null || user.IsBlocked || result == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid username or password." });

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.Password = hasher.HashPassword(user, dto.Password);
            await context.SaveChangesAsync();
        }
        return Ok(new
        {
            token = tokens.GenerateToken(user),
            expiresInSeconds = tokens.LifetimeMinutes * 60,
            user = new { user.Id, user.Username, user.Email, user.Role }
        });
    }
}
