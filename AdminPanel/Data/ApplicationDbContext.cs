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

    protected override void OnModelCreating(ModelBuilder model)
    {
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
