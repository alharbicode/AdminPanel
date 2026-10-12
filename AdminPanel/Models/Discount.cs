namespace AdminPanel.API.Models;

public class Discount
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public decimal Percentage { get; set; }  // مثل: 10 يعني خصم 10%

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }
}
