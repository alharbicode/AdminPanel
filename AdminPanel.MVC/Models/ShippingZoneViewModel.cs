using System.ComponentModel.DataAnnotations;

namespace AdminPanel.MVC.Models;

public sealed class ShippingZoneViewModel
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Governorate { get; set; } = "";
    [StringLength(100)] public string? City { get; set; }
    [Display(Name = "Shipping fee (YER)"), TwoDecimalPlaces, Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal Fee { get; set; }
    public bool Enabled { get; set; }
    public Guid? Version { get; set; }
}
