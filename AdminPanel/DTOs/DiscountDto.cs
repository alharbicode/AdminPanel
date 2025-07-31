namespace AdminPanel.API.DTOs;

public class DiscountDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Percentage { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
}
