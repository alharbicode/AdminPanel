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
    }
}
