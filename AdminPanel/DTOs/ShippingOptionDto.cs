namespace AdminPanel.API.DTOs;

public class ShippingOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Area { get; set; } = "";
    public decimal Price { get; set; }
}
