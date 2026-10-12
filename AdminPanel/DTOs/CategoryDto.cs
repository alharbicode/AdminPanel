using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;
public class CategoryDto
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(4000)] public string? Description { get; set; } = "";
}
