namespace AdminPanel.MVC.Models;
public class OrderItemViewModel
{
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal => Price * Quantity;
}
