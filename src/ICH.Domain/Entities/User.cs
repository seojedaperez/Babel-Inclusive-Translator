namespace ICH.Domain.Entities;

/// <summary>
/// System user with preferences and consent tracking.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PreferredLanguage { get; set; } = "en";
    public bool ConsentGiven { get; set; }
    public DateTimeOffset? ConsentGivenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }

    // Navigation
    public ICollection<Session> Sessions { get; set; } = new List<Session>();

    public void GiveConsent()
    {
        ConsentGiven = true;
        ConsentGivenAt = DateTimeOffset.UtcNow;
    }

    public void RevokeConsent()
    {
        ConsentGiven = false;
        ConsentGivenAt = null;
    }
}
