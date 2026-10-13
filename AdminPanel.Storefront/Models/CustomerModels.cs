using System.ComponentModel.DataAnnotations;
namespace AdminPanel.Storefront.Models;

public sealed class RegisterModel
{
    [Required(ErrorMessage = "RequiredField"), StringLength(100, ErrorMessage = "InvalidInput")] public string DisplayName { get; set; } = "";
    [Required(ErrorMessage = "RequiredField"), EmailAddress(ErrorMessage = "EmailInvalid"), StringLength(254, ErrorMessage = "EmailInvalid")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "RequiredField"), StringLength(128, MinimumLength = 12, ErrorMessage = "PasswordLength"), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password), ErrorMessage = "PasswordMismatch"), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}
public sealed class LoginModel
{
    [Required(ErrorMessage = "RequiredField"), EmailAddress(ErrorMessage = "EmailInvalid"), StringLength(254, ErrorMessage = "EmailInvalid")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "RequiredField"), StringLength(128, ErrorMessage = "InvalidInput"), DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
public sealed class ForgotModel
{
    [Required(ErrorMessage = "RequiredField"), EmailAddress(ErrorMessage = "EmailInvalid"), StringLength(254, ErrorMessage = "EmailInvalid")] public string Email { get; set; } = "";
}
public sealed class ResetModel
{
    [Required(ErrorMessage = "RequiredField"), EmailAddress(ErrorMessage = "EmailInvalid"), StringLength(254, ErrorMessage = "EmailInvalid")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "RequiredField"), RegularExpression("^[A-Za-z0-9_-]{43}$", ErrorMessage = "ResetInvalid")] public string Token { get; set; } = "";
    [Required(ErrorMessage = "RequiredField"), StringLength(128, MinimumLength = 12, ErrorMessage = "PasswordLength"), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password), ErrorMessage = "PasswordMismatch"), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}
public sealed class ProfileModel
{
    [Required(ErrorMessage = "RequiredField"), StringLength(100, ErrorMessage = "InvalidInput")] public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
}
public sealed class ChangePasswordModel
{
    [Required(ErrorMessage = "RequiredField"), StringLength(128, ErrorMessage = "InvalidInput"), DataType(DataType.Password)] public string CurrentPassword { get; set; } = "";
    [Required(ErrorMessage = "RequiredField"), StringLength(128, MinimumLength = 12, ErrorMessage = "PasswordLength"), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password), ErrorMessage = "PasswordMismatch"), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}
public sealed class CartEditModel
{
    [Range(1, int.MaxValue, ErrorMessage = "InvalidInput")] public int ProductId { get; set; }
    [Range(1, 99, ErrorMessage = "QuantityInvalid")] public int Quantity { get; set; } = 1;
    public Guid Version { get; set; }
    public string? ReturnUrl { get; set; }
}
public sealed record Customer(int Id, string DisplayName, string Email);
public sealed record CustomerLogin(string Token, int ExpiresInSeconds, Customer Customer);
public sealed record CartItem(int ProductId, string Name, string CategoryName, decimal Price, string Currency,
    bool HasImage, int Quantity, int MaximumQuantity, decimal LineTotal);
public sealed record CartSnapshot(Guid Version, List<CartItem> Items, int ItemCount, decimal Total,
    string Currency, bool Adjusted, bool AlreadyMerged = false)
{
    public static CartSnapshot Empty => new(Guid.Empty, [], 0, 0m, "YER", false);
}
public sealed record GuestCart(string Token, DateTimeOffset ExpiresUtc, CartSnapshot Cart);
public sealed record CartPage(CartSnapshot Cart, bool MergePending);
public sealed class CartRefreshModel
{
    [Required(ErrorMessage = "RequiredField")] public Guid? Version { get; set; }
}

