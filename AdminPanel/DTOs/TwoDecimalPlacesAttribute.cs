using System.ComponentModel.DataAnnotations;

namespace AdminPanel.API.DTOs;

[AttributeUsage(AttributeTargets.Property)]
public sealed class TwoDecimalPlacesAttribute : ValidationAttribute
{
    public TwoDecimalPlacesAttribute() : base("{0} must have at most two decimal places.") { }
    public override bool IsValid(object? value) => value is null || value is decimal number && decimal.Round(number, 2) == number;
}
