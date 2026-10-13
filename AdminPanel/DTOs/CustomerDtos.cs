using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;

public sealed class RegisterCustomerDto
{
    [Required, StringLength(100, MinimumLength = 1)] public string DisplayName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; set; } = "";
}
public sealed class CustomerLoginDto
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128)] public string Password { get; set; } = "";
}
public sealed class ForgotPasswordDto
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [RegularExpression("^(ar|en)$")] public string Culture { get; set; } = "ar";
}
public sealed class ResetPasswordDto
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, RegularExpression("^[A-Za-z0-9_-]{43}$")] public string Token { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; set; } = "";
}
public sealed class CustomerProfileDto
{
    [Required, StringLength(100, MinimumLength = 1)] public string DisplayName { get; set; } = "";
}
public sealed class ChangeCustomerPasswordDto
{
    [Required, StringLength(128)] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; set; } = "";
}
public sealed class CartMutationDto
{
    [Required] public Guid? Version { get; set; }
    [Range(1, 99)] public int Quantity { get; set; } = 1;
}
public sealed class MergeCartDto
{
    [Required, RegularExpression("^[A-Za-z0-9_-]{43}$")] public string GuestToken { get; set; } = "";
}
public sealed class CartRefreshDto
{
    [Required] public Guid? Version { get; set; }
}
public sealed record CustomerView(int Id, string DisplayName, string Email);
public sealed record CustomerLoginResult(string Token, int ExpiresInSeconds, CustomerView Customer);
public sealed record CartLine(int ProductId, string Name, string CategoryName, decimal Price, string Currency,
    bool HasImage, int Quantity, int MaximumQuantity, decimal LineTotal);
public sealed record CartView(Guid Version, IReadOnlyList<CartLine> Items, int ItemCount, decimal Total,
    string Currency, bool Adjusted, bool AlreadyMerged = false);
public sealed record GuestCartResult(string Token, DateTimeOffset ExpiresUtc, CartView Cart);

