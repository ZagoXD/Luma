using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
    public DbSet<AccountUser> AccountUsers => Set<AccountUser>();
    public DbSet<AccountSession> AccountSessions => Set<AccountSession>();
    public DbSet<AccountPhoneVerificationCode> AccountPhoneVerificationCodes => Set<AccountPhoneVerificationCode>();
    public DbSet<AccountSubscription> AccountSubscriptions => Set<AccountSubscription>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<BlockedConversation> BlockedConversations => Set<BlockedConversation>();

    public override int SaveChanges()
    {
        ApplyPrivacyIndexes();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyPrivacyIndexes();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var accountEmailConverter = ProtectedString("account.email");
        var accountCpfConverter = ProtectedString("account.cpf");
        var accountNameConverter = ProtectedString("account.full_name");
        var accountPhoneConverter = ProtectedString("account.phone");
        var userPhoneConverter = ProtectedString("user.phone");
        var displayNameConverter = ProtectedNullableString("user.display_name");
        var contraceptiveConverter = ProtectedNullableString("user.preference.contraceptive_type");
        var metadataConverter = ProtectedString("cycle_event.metadata");
        var pendingPayloadConverter = ProtectedString("pending_intent.payload");
        var messageBodyConverter = ProtectedNullableString("conversation_message.body");
        var blockedFromConverter = ProtectedString("blocked_conversation.from");
        var blockedReasonConverter = ProtectedString("blocked_conversation.reason");
        var pregnancyReferenceConverter = ProtectedNullableString("pregnancy.start_reference");

        modelBuilder.Entity<LumaUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.PhoneHash).IsUnique();
            entity.Property(user => user.PhoneNumber).HasConversion(userPhoneConverter).HasMaxLength(1024).IsRequired();
            entity.Property(user => user.PhoneHash).HasMaxLength(128).IsRequired();
            entity.Property(user => user.DisplayName).HasConversion(displayNameConverter).HasMaxLength(1024);
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
            entity.Property(preference => preference.ContraceptiveType).HasConversion(contraceptiveConverter).HasMaxLength(1024);
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
            entity.Property(ev => ev.MetadataJson).HasConversion(metadataConverter).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<Pregnancy>(entity =>
        {
            entity.ToTable("pregnancies");
            entity.HasKey(pregnancy => pregnancy.Id);
            entity.HasIndex(pregnancy => new { pregnancy.UserId, pregnancy.Status });
            entity.Property(pregnancy => pregnancy.Status).HasMaxLength(32).IsRequired();
            entity.Property(pregnancy => pregnancy.StartReference).HasConversion(pregnancyReferenceConverter).HasMaxLength(1024);
        });

        modelBuilder.Entity<PendingIntent>(entity =>
        {
            entity.ToTable("pending_intents");
            entity.HasKey(intent => intent.Id);
            entity.HasIndex(intent => new { intent.UserId, intent.Status, intent.CreatedAt });
            entity.Property(intent => intent.Intent).HasMaxLength(64).IsRequired();
            entity.Property(intent => intent.RequiredBeforeAction).HasMaxLength(64).IsRequired();
            entity.Property(intent => intent.Status).HasMaxLength(32).IsRequired();
            entity.Property(intent => intent.PayloadJson).HasConversion(pendingPayloadConverter).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<ConversationMessage>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(message => message.Id);
            entity.HasIndex(message => new { message.UserId, message.CreatedAt });
            entity.Property(message => message.Direction).HasMaxLength(16).IsRequired();
            entity.Property(message => message.Provider).HasMaxLength(32).IsRequired();
            entity.Property(message => message.ProviderMessageId).HasMaxLength(128);
            entity.Property(message => message.Body).HasConversion(messageBodyConverter).HasMaxLength(8192);
        });

        modelBuilder.Entity<AccountUser>(entity =>
        {
            entity.ToTable("account_users");
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.EmailHash).IsUnique();
            entity.HasIndex(user => user.CpfHash).IsUnique();
            entity.HasIndex(user => user.PhoneHash).IsUnique();
            entity.Property(user => user.Email).HasConversion(accountEmailConverter).HasMaxLength(1024).IsRequired();
            entity.Property(user => user.EmailHash).HasMaxLength(128).IsRequired();
            entity.Property(user => user.Cpf).HasConversion(accountCpfConverter).HasMaxLength(1024).IsRequired();
            entity.Property(user => user.CpfHash).HasMaxLength(128).IsRequired();
            entity.Property(user => user.FullName).HasConversion(accountNameConverter).HasMaxLength(1024).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(user => user.PhoneNumber).HasConversion(accountPhoneConverter).HasMaxLength(1024).IsRequired();
            entity.Property(user => user.PhoneHash).HasMaxLength(128).IsRequired();
            entity.Property(user => user.StripeCustomerId).HasMaxLength(128);
        });

        modelBuilder.Entity<AccountSession>(entity =>
        {
            entity.ToTable("account_sessions");
            entity.HasKey(session => session.Id);
            entity.HasIndex(session => session.TokenHash).IsUnique();
            entity.HasIndex(session => new { session.AccountUserId, session.ExpiresAt });
            entity.Property(session => session.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasOne(session => session.AccountUser)
                .WithMany(user => user.Sessions)
                .HasForeignKey(session => session.AccountUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccountPhoneVerificationCode>(entity =>
        {
            entity.ToTable("account_phone_verification_codes");
            entity.HasKey(code => code.Id);
            entity.HasIndex(code => new { code.AccountUserId, code.PhoneHash, code.Purpose, code.ExpiresAt });
            entity.Property(code => code.PhoneNumber).HasConversion(accountPhoneConverter).HasMaxLength(1024).IsRequired();
            entity.Property(code => code.PhoneHash).HasMaxLength(128).IsRequired();
            entity.Property(code => code.Purpose).HasMaxLength(32).IsRequired();
            entity.Property(code => code.CodeHash).HasMaxLength(128).IsRequired();
            entity.HasOne(code => code.AccountUser)
                .WithMany(user => user.PhoneVerificationCodes)
                .HasForeignKey(code => code.AccountUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccountSubscription>(entity =>
        {
            entity.ToTable("account_subscriptions");
            entity.HasKey(subscription => subscription.Id);
            entity.HasIndex(subscription => new { subscription.PhoneHash, subscription.Status, subscription.CurrentPeriodEndsAt });
            entity.Property(subscription => subscription.PhoneNumber).HasConversion(accountPhoneConverter).HasMaxLength(1024).IsRequired();
            entity.Property(subscription => subscription.PhoneHash).HasMaxLength(128).IsRequired();
            entity.Property(subscription => subscription.PlanCode).HasMaxLength(32).IsRequired();
            entity.Property(subscription => subscription.BillingInterval).HasMaxLength(32).IsRequired();
            entity.Property(subscription => subscription.Status).HasMaxLength(32).IsRequired();
            entity.Property(subscription => subscription.StripeSubscriptionId).HasMaxLength(128);
            entity.Property(subscription => subscription.StripePriceId).HasMaxLength(128);
            entity.HasOne(subscription => subscription.AccountUser)
                .WithMany(user => user.Subscriptions)
                .HasForeignKey(subscription => subscription.AccountUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.ToTable("notification_preferences");
            entity.HasKey(preference => preference.Id);
            entity.HasIndex(preference => preference.UserId).IsUnique();
            entity.Property(preference => preference.TimeZone).HasMaxLength(64).IsRequired();
            entity.HasOne(preference => preference.User)
                .WithMany()
                .HasForeignKey(preference => preference.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.ToTable("notification_deliveries");
            entity.HasKey(delivery => delivery.Id);
            entity.HasIndex(delivery => new { delivery.UserId, delivery.Type, delivery.ScheduledForDate }).IsUnique();
            entity.Property(delivery => delivery.Type).HasMaxLength(64).IsRequired();
            entity.Property(delivery => delivery.Status).HasMaxLength(32).IsRequired();
            entity.Property(delivery => delivery.Provider).HasMaxLength(32);
            entity.Property(delivery => delivery.ProviderMessageId).HasMaxLength(128);
            entity.Property(delivery => delivery.ErrorMessage).HasMaxLength(512);
            entity.HasOne(delivery => delivery.User)
                .WithMany()
                .HasForeignKey(delivery => delivery.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BlockedConversation>(entity =>
        {
            entity.ToTable("blocked_conversations");
            entity.HasKey(blocked => blocked.Id);
            entity.HasIndex(blocked => new { blocked.Provider, blocked.CreatedAt });
            entity.Property(blocked => blocked.Provider).HasMaxLength(32).IsRequired();
            entity.Property(blocked => blocked.From).HasConversion(blockedFromConverter).HasMaxLength(1024).IsRequired();
            entity.Property(blocked => blocked.FromHash).HasMaxLength(128).IsRequired();
            entity.Property(blocked => blocked.Reason).HasConversion(blockedReasonConverter).HasMaxLength(1024).IsRequired();
        });
    }

    private void ApplyPrivacyIndexes()
    {
        foreach (var entry in ChangeTracker.Entries<AccountUser>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.EmailHash = PrivacyRuntime.LookupHash(entry.Entity.Email, "account.email");
                entry.Entity.CpfHash = PrivacyRuntime.LookupHash(entry.Entity.Cpf, "account.cpf");
                entry.Entity.PhoneHash = PrivacyRuntime.LookupHash(entry.Entity.PhoneNumber, "account.phone");
            }
        }

        foreach (var entry in ChangeTracker.Entries<LumaUser>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.PhoneHash = PrivacyRuntime.LookupHash(entry.Entity.PhoneNumber, "user.phone");
            }
        }

        foreach (var entry in ChangeTracker.Entries<AccountSubscription>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.PhoneHash = PrivacyRuntime.LookupHash(entry.Entity.PhoneNumber, "account.phone");
            }
        }

        foreach (var entry in ChangeTracker.Entries<AccountPhoneVerificationCode>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.PhoneHash = PrivacyRuntime.LookupHash(entry.Entity.PhoneNumber, "account.phone");
            }
        }

        foreach (var entry in ChangeTracker.Entries<BlockedConversation>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.FromHash = PrivacyRuntime.LookupHash(entry.Entity.From, "blocked_conversation.from");
            }
        }
    }

    private static ValueConverter<string, string> ProtectedString(string purpose)
    {
        return new ValueConverter<string, string>(
            value => PrivacyRuntime.Protect(value, purpose),
            value => PrivacyRuntime.Unprotect(value, purpose));
    }

    private static ValueConverter<string?, string?> ProtectedNullableString(string purpose)
    {
        return new ValueConverter<string?, string?>(
            value => string.IsNullOrEmpty(value) ? value : PrivacyRuntime.Protect(value, purpose),
            value => string.IsNullOrEmpty(value) ? value : PrivacyRuntime.Unprotect(value, purpose));
    }
}
