namespace AdminPanel.MVC.Models;
public class OrderViewModel
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public string? CustomerEmail { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime OrderDate { get; set; }
    public List<OrderItemViewModel> Items { get; set; } = new();
    public decimal TotalPrice { get; set; }
    public decimal TotalToDisplay => TotalPrice;
}
