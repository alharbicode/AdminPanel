namespace AdminPanel.API.DTOs;
public class OrderDetailsDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public decimal TotalPrice { get; set; }
    public string Status { get; set; } = "";
    public string? Reference { get; set; }
    public bool IsGuest { get; set; }
    public Guid Version { get; set; }
    public string? Phone { get; set; }
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
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? StockRestoredAt { get; set; }
    public DateTime? CancellationRequestedAt { get; set; }
    public string? CancellationRequestReason { get; set; }
    public List<OrderItemViewDto> Items { get; set; } = new();
}
