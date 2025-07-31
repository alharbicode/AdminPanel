namespace AdminPanel.API.DTOs;

public class OrderDetailsDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public decimal TotalPrice { get; set; }
    public string Status { get; set; } = "";
    public List<OrderItemViewDto> Items { get; set; } = new();
    public IFormFile? ImageFile { get; set; }
}
