using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;
public class ContentPageDto
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required, StringLength(200), RegularExpression(@"[A-Za-z0-9]+(?:[- ][A-Za-z0-9]+)*")]
    public string Slug { get; set; } = "";
    [Required, StringLength(100000)] public string Content { get; set; } = "";
}
