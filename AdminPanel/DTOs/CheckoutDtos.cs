using System.ComponentModel.DataAnnotations;

namespace AdminPanel.API.DTOs;

public class CheckoutQuoteDto
{
    [Required] public Guid? CartVersion { get; set; }
    [Range(1, int.MaxValue)] public int ShippingZoneId { get; set; }
    [Required, StringLength(100, MinimumLength = 1)] public string ContactName { get; set; } = "";
    [Required, StringLength(30), RegularExpression(@"^\+?(?=(?:\D*\d){7,15}\D*$)[0-9][0-9 ()-]{6,28}$")] public string Phone { get; set; } = "";
    [EmailAddress, StringLength(254)] public string? Email { get; set; }
    [Required, StringLength(100, MinimumLength = 1)] public string City { get; set; } = "";
    [Required, StringLength(500, MinimumLength = 5)] public string AddressLine { get; set; } = "";
    [StringLength(1000)] public string? DeliveryInstructions { get; set; }
}
public sealed class CheckoutConfirmDto : CheckoutQuoteDto
{
    [Required] public Guid? IdempotencyKey { get; set; }
    [Required, RegularExpression("^[A-Fa-f0-9]{64}$")] public string QuoteFingerprint { get; set; } = "";
}
public sealed class OrderActionDto
{
    [Required] public Guid? Version { get; set; }
    [Required, StringLength(500, MinimumLength = 1)] public string Reason { get; set; } = "";
}
public sealed record ShippingDestination(int Id, string Governorate, string City, decimal Fee, string Currency);
public sealed record CheckoutLine(int ProductId, string ProductName, int Quantity, decimal OriginalUnitPrice,
    int? DiscountId, string? DiscountName, decimal DiscountPercentage, decimal UnitDiscount, decimal UnitPrice, decimal LineTotal);
public sealed record CheckoutQuote(Guid CartVersion, string QuoteFingerprint, int ShippingZoneId, string Governorate, string City,
    IReadOnlyList<CheckoutLine> Items, decimal Subtotal, decimal DiscountTotal, decimal ShippingFee, decimal Total,
    string Currency, string PaymentMethod);
public sealed record CheckoutResult(OrderView Order, string? GuestTrackingToken, bool Replayed);
public sealed record OrderView(int Id, string Reference, Guid Version, DateTime OrderDate, string Status,
    string ContactName, string Phone, string? Email, string Governorate, string City, string AddressLine, string? DeliveryInstructions,
    IReadOnlyList<CheckoutLine> Items, decimal Subtotal, decimal DiscountTotal, decimal ShippingFee, decimal Total,
    string Currency, string PaymentMethod, bool CanCancel, bool CanRequestCancellation, DateTime? CancelledAt,
    string? CancellationReason, DateTime? CancellationRequestedAt, string? CancellationRequestReason);
public sealed record CustomerOrderPage(IReadOnlyList<OrderView> Items, int TotalCount, int PageNumber, int PageSize);
