using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Data;

public sealed class LumaDbContext(DbContextOptions<LumaDbContext> options) : DbContext(options)
{
    public DbSet<LumaUser> Users => Set<LumaUser>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<ConsentRecord> Consents => Set<ConsentRecord>();
    public DbSet<Cycle> Cycles => Set<Cycle>();
    public DbSet<CycleEvent> CycleEvents => Set<CycleEvent>();
    public DbSet<Pregnancy> Pregnancies => Set<Pregnancy>();
    public DbSet<PendingIntent> PendingIntents => Set<PendingIntent>();
    public DbSet<ConversationMessage> Messages => Set<ConversationMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LumaUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.PhoneNumber).IsUnique();
            entity.Property(user => user.PhoneNumber).HasMaxLength(64).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(120);
            entity.Property(user => user.OnboardingStep).HasMaxLength(64).IsRequired();
            entity.Property(user => user.PendingAction).HasMaxLength(64);
            entity.HasOne(user => user.Preference)
                .WithOne(preference => preference.User)
                .HasForeignKey<UserPreference>(preference => preference.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.ToTable("user_preferences");
            entity.HasKey(preference => preference.Id);
            entity.HasIndex(preference => preference.UserId).IsUnique();
            entity.Property(preference => preference.Language).HasMaxLength(16).IsRequired();
            entity.Property(preference => preference.ContraceptiveType).HasMaxLength(64);
        });

        modelBuilder.Entity<ConsentRecord>(entity =>
        {
            entity.ToTable("consents");
            entity.HasKey(consent => consent.Id);
            entity.HasIndex(consent => new { consent.UserId, consent.ConsentType });
            entity.Property(consent => consent.ConsentType).HasMaxLength(80).IsRequired();
            entity.Property(consent => consent.Version).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<Cycle>(entity =>
        {
            entity.ToTable("cycles");
            entity.HasKey(cycle => cycle.Id);
            entity.HasIndex(cycle => new { cycle.UserId, cycle.StartDate });
            entity.Property(cycle => cycle.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<CycleEvent>(entity =>
        {
            entity.ToTable("cycle_events");
            entity.HasKey(ev => ev.Id);
            entity.HasIndex(ev => new { ev.UserId, ev.Date, ev.Type });
            entity.Property(ev => ev.Type).HasMaxLength(64).IsRequired();
            entity.Property(ev => ev.Source).HasMaxLength(32).IsRequired();
            entity.Property(ev => ev.MetadataJson).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<Pregnancy>(entity =>
        {
            entity.ToTable("pregnancies");
            entity.HasKey(pregnancy => pregnancy.Id);
            entity.HasIndex(pregnancy => new { pregnancy.UserId, pregnancy.Status });
            entity.Property(pregnancy => pregnancy.Status).HasMaxLength(32).IsRequired();
            entity.Property(pregnancy => pregnancy.StartReference).HasMaxLength(64);
        });

        modelBuilder.Entity<PendingIntent>(entity =>
        {
            entity.ToTable("pending_intents");
            entity.HasKey(intent => intent.Id);
            entity.HasIndex(intent => new { intent.UserId, intent.Status, intent.CreatedAt });
            entity.Property(intent => intent.Intent).HasMaxLength(64).IsRequired();
            entity.Property(intent => intent.RequiredBeforeAction).HasMaxLength(64).IsRequired();
            entity.Property(intent => intent.Status).HasMaxLength(32).IsRequired();
            entity.Property(intent => intent.PayloadJson).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<ConversationMessage>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(message => message.Id);
            entity.HasIndex(message => new { message.UserId, message.CreatedAt });
            entity.Property(message => message.Direction).HasMaxLength(16).IsRequired();
            entity.Property(message => message.Provider).HasMaxLength(32).IsRequired();
            entity.Property(message => message.ProviderMessageId).HasMaxLength(128);
            entity.Property(message => message.Body).HasMaxLength(4096);
        });
    }
}
