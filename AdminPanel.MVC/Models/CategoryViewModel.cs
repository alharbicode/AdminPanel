using System.ComponentModel.DataAnnotations;

namespace AdminPanel.MVC.Models;

public class CategoryViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Please enter category name")]
    public string Name { get; set; } = "";

    public string? Description { get; set; }
}
