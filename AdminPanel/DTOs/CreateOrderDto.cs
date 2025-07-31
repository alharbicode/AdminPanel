namespace AdminPanel.API.DTOs;

public class CreateOrderDto
{
    public int CustomerId { get; set; }
    public int? ShippingOptionId { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}
