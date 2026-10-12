using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;
public class CreateOrderDto
{
    [Range(1, int.MaxValue)] public int CustomerId { get; set; }
    [Range(1, int.MaxValue)] public int? ShippingOptionId { get; set; }
    [Required, MinLength(1), MaxLength(100)] public List<OrderItemDto> Items { get; set; } = new();
}
