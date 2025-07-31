namespace AdminPanel.API.DTOs;

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = "";
    public int StockQuantity { get; set; }
    public string Unit { get; set; } = "Piece";
    public bool IsAvailable { get; set; }
    public int CategoryId { get; set; }
    public IFormFile? ImageFile { get; set; }
}
