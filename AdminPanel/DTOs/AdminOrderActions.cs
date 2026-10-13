using System.ComponentModel.DataAnnotations;

namespace AdminPanel.API.DTOs;

public sealed class AdminOrderTransition
{
    [Required] public Guid? Version { get; set; }
    [Required, RegularExpression("^(Confirmed|Preparing|Shipped|Delivered)$")] public string Status { get; set; } = "";
}
public sealed class AdminOrderCancellation
{
    [Required] public Guid? Version { get; set; }
    [Required, StringLength(500)] public string Reason { get; set; } = "";
}
