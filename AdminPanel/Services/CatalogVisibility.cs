using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.EntityFrameworkCore;
namespace AdminPanel.API.Services;

public sealed class CatalogVisibility(ApplicationDbContext db, IConfiguration configuration)
{
    public IQueryable<Product> Products()
    {
        var approved = configuration.GetSection("PublicCatalog:ProductIds").Get<int[]>() ?? [];
        return db.Products.AsNoTracking().Where(p => approved.Contains(p.Id) &&
            p.IsAvailable && p.StockQuantity > 0 && p.Price >= 0 && p.Category != null);
    }
}

