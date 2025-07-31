using Microsoft.AspNetCore.Http;

namespace AdminPanel.MVC.Models;
using System.ComponentModel.DataAnnotations;

public class ProductViewModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string Unit { get; set; }
    public bool IsAvailable { get; set; }
    [Required(ErrorMessage = "Category is required")]
    public int CategoryId { get; set; }

    public string? CategoryName { get; set; }
    public IFormFile? ImageFile { get; set; }
    public string? ImageUrl { get; set; } // Added missing property to fix CS1061  
}
