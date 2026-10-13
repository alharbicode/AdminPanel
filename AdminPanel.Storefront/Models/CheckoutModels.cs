using System.ComponentModel.DataAnnotations;

namespace AdminPanel.Storefront.Models;

public sealed class CheckoutModel
{
    [Required(ErrorMessage = "RequiredField")] public Guid? CartVersion { get; set; }
    [Required(ErrorMessage = "RequiredField")] public Guid? IdempotencyKey { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "SelectDestination")] public int ShippingZoneId { get; set; }
    [Required(ErrorMessage = "RequiredField"), StringLength(100, ErrorMessage = "InvalidInput")] public string ContactName { get; set; } = "";
    [Required(ErrorMessage = "RequiredField"), StringLength(30, ErrorMessage = "PhoneInvalid"), RegularExpression(@"^\+?(?=(?:\D*\d){7,15}\D*$)[0-9][0-9 ()-]{6,28}$", ErrorMessage = "PhoneInvalid")] public string Phone { get; set; } = "";
    [EmailAddress(ErrorMessage = "EmailInvalid"), StringLength(254, ErrorMessage = "EmailInvalid")] public string? Email { get; set; }
    [Required(ErrorMessage = "RequiredField"), StringLength(100, ErrorMessage = "InvalidInput")] public string City { get; set; } = "";
    [Required(ErrorMessage = "RequiredField"), StringLength(500, MinimumLength = 5, ErrorMessage = "AddressInvalid")] public string AddressLine { get; set; } = "";
    [StringLength(1000, ErrorMessage = "InvalidInput")] public string? DeliveryInstructions { get; set; }
}
public sealed class TrackOrderModel
{
    [Required(ErrorMessage = "RequiredField"), RegularExpression("^[A-Za-z0-9_-]{43}$", ErrorMessage = "OrderNotFound")]
    public string TrackingCode { get; set; } = "";
}
public sealed class OrderActionModel
{
    public int Id { get; set; }
    public bool Guest { get; set; }
    [Required(ErrorMessage = "RequiredField")] public Guid? Version { get; set; }
    [Required(ErrorMessage = "RequiredField"), StringLength(500, ErrorMessage = "InvalidInput")]
    public string Reason { get; set; } = "";
}
public sealed record ShippingDestination(int Id, string Governorate, string City, decimal Fee, string Currency);
public sealed record CheckoutItem(int ProductId, string ProductName, int Quantity, decimal OriginalUnitPrice,
    int? DiscountId, string? DiscountName, decimal DiscountPercentage, decimal UnitDiscount, decimal UnitPrice, decimal LineTotal);
public sealed record CheckoutQuote(Guid CartVersion, string QuoteFingerprint, int ShippingZoneId, string Governorate, string City,
    List<CheckoutItem> Items, decimal Subtotal, decimal DiscountTotal, decimal ShippingFee, decimal Total,
    string Currency, string PaymentMethod);
public sealed record OrderSnapshot(int Id, string Reference, Guid Version, DateTime OrderDate, string Status,
    string ContactName, string Phone, string? Email, string Governorate, string City, string AddressLine, string? DeliveryInstructions,
    List<CheckoutItem> Items, decimal Subtotal, decimal DiscountTotal, decimal ShippingFee, decimal Total,
    string Currency, string PaymentMethod, bool CanCancel, bool CanRequestCancellation, DateTime? CancelledAt,
    string? CancellationReason, DateTime? CancellationRequestedAt, string? CancellationRequestReason);
public sealed record CheckoutResult(OrderSnapshot Order, string? GuestTrackingToken, bool Replayed);
public sealed record CustomerOrders(List<OrderSnapshot> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
public sealed record CheckoutPage(CheckoutModel Form, List<ShippingDestination> Destinations, CartSnapshot Cart);
public sealed record ReviewState(CheckoutModel Form, CheckoutQuote Quote, string Owner);
public sealed record ReviewPage(ReviewState State, string ReviewToken);
public sealed record OrderPage(OrderSnapshot Order, bool Guest, bool Confirmed = false, string? TrackingCode = null);
public sealed record OrderTotals(decimal Subtotal, decimal DiscountTotal, decimal ShippingFee, decimal Total);
