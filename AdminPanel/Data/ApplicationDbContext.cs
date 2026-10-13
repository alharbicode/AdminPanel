using AdminPanel.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ShippingOption> ShippingOptions => Set<ShippingOption>();
    public DbSet<ShippingZone> ShippingZones => Set<ShippingZone>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<ContentPage> ContentPages => Set<ContentPage>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<ShoppingCart> ShoppingCarts => Set<ShoppingCart>();
    public DbSet<ShoppingCartItem> ShoppingCartItems => Set<ShoppingCartItem>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CustomerAccount>().HasKey(a => a.UserId);
        model.Entity<CustomerAccount>().HasOne(a => a.User).WithOne().HasForeignKey<CustomerAccount>(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CustomerAccount>().Property(a => a.NormalizedEmail).HasMaxLength(254);
        model.Entity<CustomerAccount>().HasIndex(a => a.NormalizedEmail).IsUnique();
        model.Entity<CustomerAccount>().Property(a => a.DisplayName).HasMaxLength(100);
        model.Entity<CustomerAccount>().Property(a => a.ResetTokenHash).HasMaxLength(64);
        model.Entity<CustomerAccount>().Property(a => a.Version).IsConcurrencyToken();
        model.Entity<ShoppingCart>().HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ShoppingCart>().HasIndex(c => c.CustomerId).IsUnique().HasFilter("[CustomerId] IS NOT NULL");
        model.Entity<ShoppingCart>().Property(c => c.GuestTokenHash).HasMaxLength(64);
        model.Entity<ShoppingCart>().HasIndex(c => c.GuestTokenHash).IsUnique().HasFilter("[GuestTokenHash] IS NOT NULL");
        model.Entity<ShoppingCart>().Property(c => c.Version).IsConcurrencyToken();
        model.Entity<ShoppingCartItem>().HasKey(i => new { i.CartId, i.ProductId });
        model.Entity<ShoppingCartItem>().HasOne(i => i.Cart).WithMany(c => c.Items).HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        // Cart entries must not prevent the existing administrator product deletion workflow.
        model.Entity<ShoppingCartItem>().HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<User>().Property(u => u.Username).HasMaxLength(256);
        model.Entity<User>().HasIndex(u => u.Username).IsUnique();
        model.Entity<ContentPage>().Property(p => p.Slug).HasMaxLength(200);
        model.Entity<ContentPage>().HasIndex(p => p.Slug).IsUnique();
        model.Entity<Product>().Property(p => p.StockQuantity).IsConcurrencyToken();
        model.Entity<Order>().Property(o => o.Status).IsConcurrencyToken();
        model.Entity<Order>().Property(o => o.Version).IsConcurrencyToken();
        model.Entity<Order>().Property(o => o.Reference).HasMaxLength(32);
        model.Entity<Order>().HasIndex(o => o.Reference).IsUnique().HasFilter("[Reference] IS NOT NULL");
        model.Entity<Order>().Property(o => o.CheckoutOwner).HasMaxLength(80);
        model.Entity<Order>().HasIndex(o => new { o.CheckoutOwner, o.IdempotencyKey }).IsUnique()
            .HasFilter("[CheckoutOwner] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
        model.Entity<Order>().Property(o => o.CheckoutRequestHash).HasMaxLength(64);
        model.Entity<Order>().Property(o => o.GuestTrackingHash).HasMaxLength(64);
        model.Entity<Order>().HasIndex(o => o.GuestTrackingHash).IsUnique().HasFilter("[GuestTrackingHash] IS NOT NULL");
        model.Entity<Order>().Property(o => o.ContactName).HasMaxLength(100);
        model.Entity<Order>().Property(o => o.ContactEmail).HasMaxLength(254);
        model.Entity<Order>().Property(o => o.Phone).HasMaxLength(30);
        model.Entity<Order>().Property(o => o.CountryCode).HasMaxLength(2);
        model.Entity<Order>().Property(o => o.Governorate).HasMaxLength(100);
        model.Entity<Order>().Property(o => o.City).HasMaxLength(100);
        model.Entity<Order>().Property(o => o.AddressLine).HasMaxLength(500);
        model.Entity<Order>().Property(o => o.DeliveryInstructions).HasMaxLength(1000);
        model.Entity<Order>().Property(o => o.Currency).HasMaxLength(3);
        model.Entity<Order>().Property(o => o.PaymentMethod).HasMaxLength(10);
        model.Entity<Order>().Property(o => o.CancelledBy).HasMaxLength(80);
        model.Entity<Order>().Property(o => o.CancellationReason).HasMaxLength(500);
        model.Entity<Order>().Property(o => o.CancellationRequestReason).HasMaxLength(500);
        model.Entity<OrderItem>().Property(i => i.ProductName).HasMaxLength(200);
        model.Entity<OrderItem>().Property(i => i.DiscountName).HasMaxLength(200);
        model.Entity<ShippingZone>().Property(z => z.Governorate).HasMaxLength(100);
        model.Entity<ShippingZone>().Property(z => z.City).HasMaxLength(100);
        model.Entity<ShippingZone>().Property(z => z.NormalizedDestination).HasMaxLength(401);
        model.Entity<ShippingZone>().HasIndex(z => z.NormalizedDestination).IsUnique();
        model.Entity<ShippingZone>().Property(z => z.Version).IsConcurrencyToken();
        model.Entity<Product>().HasOne(p => p.Category).WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrderItem>().HasOne(i => i.Product).WithMany()
            .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Order>().HasOne(o => o.Customer).WithMany()
            .HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Discount>().HasOne(d => d.Product).WithMany()
            .HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Product>().Property(p => p.Price).HasPrecision(18, 2);
        model.Entity<Order>().Property(o => o.TotalPrice).HasPrecision(18, 2);
        model.Entity<OrderItem>().Property(i => i.Price).HasPrecision(18, 2);
        model.Entity<ShippingOption>().Property(s => s.Price).HasPrecision(18, 2);
        model.Entity<Discount>().Property(d => d.Percentage).HasPrecision(18, 2);
        model.Entity<ShippingZone>().Property(z => z.Fee).HasPrecision(18, 2);
        model.Entity<Order>().Property(o => o.Subtotal).HasPrecision(18, 2);
        model.Entity<Order>().Property(o => o.DiscountTotal).HasPrecision(18, 2);
        model.Entity<Order>().Property(o => o.ShippingFee).HasPrecision(18, 2);
        model.Entity<OrderItem>().Property(i => i.OriginalUnitPrice).HasPrecision(18, 2);
        model.Entity<OrderItem>().Property(i => i.DiscountPercentage).HasPrecision(18, 2);
        model.Entity<OrderItem>().Property(i => i.UnitDiscount).HasPrecision(18, 2);
    }
}
