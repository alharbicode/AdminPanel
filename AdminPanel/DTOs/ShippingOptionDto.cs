using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;
public class ShippingOptionDto
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Required, StringLength(200)] public string Area { get; set; } = "";
    [TwoDecimalPlaces, Range(typeof(decimal), "0", "9999999999999999.99")] public decimal Price { get; set; }
}
