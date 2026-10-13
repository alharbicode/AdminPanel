namespace AdminPanel.API.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; }
    public decimal Price { get; set; } // السعر وقت الشراء (قد يختلف عن السعر الحالي للمنتج)
    public string? ProductName { get; set; }
    public decimal? OriginalUnitPrice { get; set; }
    public int? AppliedDiscountId { get; set; }
    public string? DiscountName { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? UnitDiscount { get; set; }
}
