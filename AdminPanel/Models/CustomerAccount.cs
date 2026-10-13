namespace AdminPanel.API.Models;

// Customer enrollment is separate from legacy administrator/staff records.
public sealed class CustomerAccount
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string NormalizedEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public string? ResetTokenHash { get; set; }
    public long? ResetExpiresUtcTicks { get; set; }
    public int FailedLogins { get; set; }
    public long? LockedUntilUtcTicks { get; set; }
}

