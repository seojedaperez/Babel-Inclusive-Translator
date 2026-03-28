using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace ICH.WebPortal.Models;

public class SignModel
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string WeightsBase64 { get; set; } = string.Empty;
}

public class TranscriptSession
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Content { get; set; } = string.Empty;
}

public class AppDbContext : DbContext
{
    public DbSet<SignModel> Signs { get; set; }
    public DbSet<TranscriptSession> Transcripts { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SignModel>()
            .ToContainer("Signs")
            .HasPartitionKey(c => c.UserId);

        modelBuilder.Entity<TranscriptSession>()
            .ToContainer("Transcripts")
            .HasPartitionKey(c => c.UserId);
    }
}
