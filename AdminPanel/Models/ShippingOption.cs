namespace AdminPanel.API.Models;

public class ShippingOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";      // مثل: شحن سريع، شحن مجاني
    public string Area { get; set; } = "";      // المنطقة: صنعاء، عدن، إلخ
    public decimal Price { get; set; }          // السعر الخاص بهذه المنطقة/الخدمة
}
