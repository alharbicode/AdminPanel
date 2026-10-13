namespace AdminPanel.API.Models;

public sealed class ShippingZone
{
    public int Id { get; set; }
    public string Governorate { get; set; } = "";
    // An empty city means the configured fee applies throughout this governorate.
    public string City { get; set; } = "";
    public string NormalizedDestination { get; set; } = "";
    public decimal Fee { get; set; }
    public bool Enabled { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
