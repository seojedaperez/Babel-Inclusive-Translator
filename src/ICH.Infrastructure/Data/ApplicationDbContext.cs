using Microsoft.EntityFrameworkCore;
using ICH.Domain.Entities;

namespace ICH.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<TranscriptEntry> TranscriptEntries => Set<TranscriptEntry>();
    public DbSet<User> Users => Set<User>();
    public DbSet<SessionEvent> SessionEvents => Set<SessionEvent>();

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Session
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.SourceLanguage).HasMaxLength(10).IsRequired();
            entity.Property(e => e.TargetLanguage).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Summary).HasMaxLength(10000);
            entity.Property(e => e.RecordingBlobUrl).HasMaxLength(2000);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Sessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.StartedAt);
        });

        // TranscriptEntry
        modelBuilder.Entity<TranscriptEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalText).IsRequired();
            entity.Property(e => e.TranslatedText).IsRequired();
            entity.Property(e => e.SourceLanguage).HasMaxLength(10).IsRequired();
            entity.Property(e => e.TargetLanguage).HasMaxLength(10).IsRequired();
            entity.Property(e => e.SpeakerId).HasMaxLength(100);
            entity.Property(e => e.SpeakerName).HasMaxLength(200);
            entity.Property(e => e.Emotion).HasMaxLength(50);
            entity.Property(e => e.PipelineType).HasMaxLength(20).IsRequired();

            entity.HasOne(e => e.Session)
                .WithMany(s => s.TranscriptEntries)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.Timestamp);
        });

        // User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(e => e.PreferredLanguage).HasMaxLength(10).HasDefaultValue("en");
            entity.Property(e => e.RefreshToken).HasMaxLength(500);

            entity.HasIndex(e => e.Email).IsUnique();
        });

        // SessionEvent
        modelBuilder.Entity<SessionEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Data).HasMaxLength(5000);

            entity.HasOne(e => e.Session)
                .WithMany(s => s.Events)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.SessionId);
        });
    }
}
