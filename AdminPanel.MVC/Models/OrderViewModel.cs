using System.ComponentModel.DataAnnotations;

public class OrderViewModel
{
    public int Id { get; set; }

    public string CustomerName { get; set; } = "";
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerAddress { get; set; }

    [Display(Name = "Status")]
    public string Status { get; set; } = "Pending";

    // التاريخ  
    public DateTime OrderDate { get; set; }

    // عناصر الطلب  
    public List<OrderItemViewModel> Items { get; set; } = new();

    // السعر الإجمالي (إذا لم يأتِ من API نحسبه)  
    public decimal? TotalPriceFromApi { get; set; }

    public decimal ComputedTotal =>
        Items?.Sum(i => i.Total) ?? 0m; // Fixed: Changed 'LineTotal' to 'Total'  

    public decimal TotalToDisplay =>
        TotalPriceFromApi.HasValue && TotalPriceFromApi.Value > 0
            ? TotalPriceFromApi.Value
            : ComputedTotal;    public class OrderItemViewModel
    {
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Total => Quantity * UnitPrice;
    }
}