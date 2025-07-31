namespace AdminPanel.API.DTOs;

public class CreateDiscountDto
{
    public string Name { get; set; } = "";
    public decimal Percentage { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int ProductId { get; set; }
}
