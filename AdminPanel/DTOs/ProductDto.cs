using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;
public class ProductDto
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(4000)] public string? Description { get; set; } = "";
    [TwoDecimalPlaces, Range(typeof(decimal), "0", "9999999999999999.99")] public decimal Price { get; set; }
    public string ImageUrl { get; set; } = "";
    [Range(0, int.MaxValue)] public int StockQuantity { get; set; }
    [Range(0, int.MaxValue)] public int? ExpectedStockQuantity { get; set; }
    [Required, StringLength(50)] public string Unit { get; set; } = "Piece";
    public bool IsAvailable { get; set; }
    [Range(1, int.MaxValue)] public int CategoryId { get; set; }
    public IFormFile? ImageFile { get; set; }
}
