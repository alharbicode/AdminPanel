namespace AdminPanel.API.Models;

public sealed class ShoppingCart
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? CustomerId { get; set; }
    public CustomerAccount? Customer { get; set; }
    public string? GuestTokenHash { get; set; }
    public long? ExpiresUtcTicks { get; set; }
    public int? MergedToCustomerId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<ShoppingCartItem> Items { get; set; } = [];
}
public sealed class ShoppingCartItem
{
    public Guid CartId { get; set; }
    public ShoppingCart Cart { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
}

