using AdminPanel.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace AdminPanel.API.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ShippingOption> ShippingOptions => Set<ShippingOption>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<ContentPage> ContentPages => Set<ContentPage>();


}
