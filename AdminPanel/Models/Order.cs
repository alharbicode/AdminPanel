namespace AdminPanel.API.Models;

public class Order
{
    public int Id { get; set; }
    public int? CustomerId { get; set; }
    public User? Customer { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public decimal TotalPrice { get; set; }
    public string Status { get; set; } = "Pending";
    public int? ShippingOptionId { get; set; }
    public ShippingOption? ShippingOption { get; set; }
    // Nullable snapshots distinguish new storefront orders from unchanged legacy history.
    public string? Reference { get; set; }
    public string? CheckoutOwner { get; set; }
    public Guid? IdempotencyKey { get; set; }
    public string? CheckoutRequestHash { get; set; }
    public string? GuestTrackingHash { get; set; }
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? ContactEmail { get; set; }
    public string? CountryCode { get; set; }
    public string? Governorate { get; set; }
    public string? City { get; set; }
    public string? AddressLine { get; set; }
    public string? DeliveryInstructions { get; set; }
    public string? Currency { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? DiscountTotal { get; set; }
    public decimal? ShippingFee { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? StockRestoredAt { get; set; }
    public DateTime? CancellationRequestedAt { get; set; }
    public string? CancellationRequestReason { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
