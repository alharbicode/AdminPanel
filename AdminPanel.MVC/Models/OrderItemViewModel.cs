namespace AdminPanel.MVC.Models;
public class OrderItemViewModel
{
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public decimal? OriginalUnitPrice { get; set; }
    public decimal? UnitDiscount { get; set; }
    public string? DiscountName { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal => Price * Quantity;
}
