namespace AdminPanel.API.DTOs;

public class OrderItemViewDto
{
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
