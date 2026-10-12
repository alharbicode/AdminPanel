namespace AdminPanel.API.Models;

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public User? Customer { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public decimal TotalPrice { get; set; }
    public string Status { get; set; } = "Pending";
    public int? ShippingOptionId { get; set; }
    public ShippingOption? ShippingOption { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
