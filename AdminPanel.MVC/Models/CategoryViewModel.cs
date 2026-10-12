using System.ComponentModel.DataAnnotations;

namespace AdminPanel.MVC.Models;

public class CategoryViewModel
{
    public int Id { get; set; }

    [StringLength(200)]
    [Required(ErrorMessage = "Please enter category name")]
    public string Name { get; set; } = "";

    [StringLength(4000)]
    public string? Description { get; set; }
}
