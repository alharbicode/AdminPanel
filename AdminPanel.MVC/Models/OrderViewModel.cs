namespace AdminPanel.MVC.Models;
public class OrderViewModel
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public string? CustomerEmail { get; set; }
    public string Status { get; set; } = "Pending";
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
    public string? NextStatus => Status switch
    {
        "Pending" => "Confirmed", "Confirmed" => "Preparing", "Preparing" => "Shipped", "Shipped" => "Delivered", _ => null
    };
    public bool CanCancel => Status is "Pending" or "Confirmed" or "Preparing";
    public DateTime OrderDate { get; set; }
    public List<OrderItemViewModel> Items { get; set; } = new();
    public decimal TotalPrice { get; set; }
    public decimal TotalToDisplay => TotalPrice;
}
