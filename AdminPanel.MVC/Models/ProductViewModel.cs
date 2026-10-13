using System.ComponentModel.DataAnnotations;
namespace AdminPanel.MVC.Models;
public class ProductViewModel
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(4000)] public string? Description { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal Price { get; set; }
    [Display(Name = "Stock quantity"), Range(0, int.MaxValue)] public int StockQuantity { get; set; }
    [Range(0, int.MaxValue)] public int? ExpectedStockQuantity { get; set; }
    [Required, StringLength(50)] public string Unit { get; set; } = "Piece";
    [Display(Name = "Available")] public bool IsAvailable { get; set; } = true;
    [Display(Name = "Category"), Range(1, int.MaxValue, ErrorMessage = "Category is required")] public int CategoryId { get; set; }
    public IFormFile? ImageFile { get; set; }
    public string? ImageUrl { get; set; }
}
