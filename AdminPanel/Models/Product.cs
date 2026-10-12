namespace AdminPanel.API.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = "";
    public int StockQuantity { get; set; }
    public string Unit { get; set; } = "Piece"; // كيلو، كرتون...
    public bool IsAvailable { get; set; } = true;
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}
