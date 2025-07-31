namespace AdminPanel.MVC.Models;

public class OrderItemViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}
