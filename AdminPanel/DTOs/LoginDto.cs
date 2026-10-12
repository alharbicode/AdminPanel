using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;
public class LoginDto
{
    [Required, StringLength(256)] public string Username { get; set; } = "";
    [Required, StringLength(1024)] public string Password { get; set; } = "";
}
