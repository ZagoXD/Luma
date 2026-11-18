using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public sealed class NotificationStatusService(LumaDbContext db)
{
    public async Task<bool> ApplyAsync(Guid deliveryId, string messageSid, string providerStatus, string? errorCode,
        CancellationToken cancellationToken = default)
    {
        var status = providerStatus switch {
            "accepted" or "queued" or "sending" => NotificationDeliveryStatuses.Submitted,
            "sent" => NotificationDeliveryStatuses.Sent,
            "delivered" => NotificationDeliveryStatuses.Delivered,
            "read" => NotificationDeliveryStatuses.Read,
            "failed" or "undelivered" or "canceled" => NotificationDeliveryStatuses.Failed,
            _ => null
        };
        if (status is null || !(messageSid.StartsWith("SM", StringComparison.Ordinal)
            || messageSid.StartsWith("MM", StringComparison.Ordinal)) || messageSid.Length > 128) return false;
        var delivery = await db.NotificationDeliveries.FindAsync([deliveryId], cancellationToken);
        if (delivery is null || (delivery.ProviderMessageId is not null && delivery.ProviderMessageId != messageSid)) return false;
        if (Rank(status) < Rank(delivery.Status)) return true;
        delivery.ProviderMessageId = messageSid;
        delivery.Status = status;
        delivery.NextAttemptAt = null;
        delivery.UpdatedAt = DateTimeOffset.UtcNow;
        if (status is NotificationDeliveryStatuses.Sent or NotificationDeliveryStatuses.Delivered or NotificationDeliveryStatuses.Read)
            delivery.SentAt ??= DateTimeOffset.UtcNow;
        delivery.ErrorMessage = status == NotificationDeliveryStatuses.Failed && int.TryParse(errorCode, out var code) ? code.ToString() : null;
        if (db.Database.IsRelational())
        {
            var allowed = new[] { NotificationDeliveryStatuses.Pending, NotificationDeliveryStatuses.Unknown,
                NotificationDeliveryStatuses.Submitted, NotificationDeliveryStatuses.Sent, NotificationDeliveryStatuses.Failed,
                NotificationDeliveryStatuses.Delivered, NotificationDeliveryStatuses.Read }.Where(item => Rank(item) <= Rank(status)).ToArray();
            await db.NotificationDeliveries.Where(item => item.Id == deliveryId && allowed.Contains(item.Status)
                && (item.ProviderMessageId == null || item.ProviderMessageId == messageSid))
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, status)
                    .SetProperty(item => item.ProviderMessageId, messageSid)
                    .SetProperty(item => item.ErrorMessage, delivery.ErrorMessage)
                    .SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.SentAt, item => item.SentAt ?? delivery.SentAt)
                    .SetProperty(item => item.UpdatedAt, delivery.UpdatedAt), cancellationToken);
            await db.Entry(delivery).ReloadAsync(cancellationToken);
        }
        else await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static int Rank(string status) => status switch {
        NotificationDeliveryStatuses.Read => 5,
        NotificationDeliveryStatuses.Delivered => 4,
        NotificationDeliveryStatuses.Failed => 3,
        NotificationDeliveryStatuses.Sent => 2,
        NotificationDeliveryStatuses.Submitted => 1,
        _ => 0
    };
}
