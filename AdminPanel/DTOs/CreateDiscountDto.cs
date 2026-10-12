using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;
public class CreateDiscountDto : IValidatableObject
{
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Range(typeof(decimal), "0", "100")] public decimal Percentage { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (StartDate == default || EndDate <= StartDate)
            yield return new ValidationResult("End date must be after a valid start date.", new[] { nameof(EndDate) });
    }
}
