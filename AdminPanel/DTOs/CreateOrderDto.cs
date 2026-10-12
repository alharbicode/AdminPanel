using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;
public class CreateOrderDto : IValidatableObject
{
    [Range(1, int.MaxValue)] public int CustomerId { get; set; }
    [Range(1, int.MaxValue)] public int? ShippingOptionId { get; set; }
    [Required, MinLength(1), MaxLength(100)] public List<OrderItemDto> Items { get; set; } = new();
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Items is not null && Items.Any(item => item is null))
            yield return new ValidationResult("Order lines cannot be null.", new[] { nameof(Items) });
    }
}

