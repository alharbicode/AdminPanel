using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;
public class OrderItemDto
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
}
