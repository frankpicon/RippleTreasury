using MassTransit;
using Microsoft.EntityFrameworkCore;
using Notifications.Worker.Domain;

namespace Notifications.Worker.Data;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
    : DbContext(options)
{
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationRecord>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Recipient).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(2_000).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.PurchaseId).IsUnique();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
