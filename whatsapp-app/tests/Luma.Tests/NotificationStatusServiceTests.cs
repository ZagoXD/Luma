using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Luma.Tests;

public sealed class NotificationStatusServiceTests
{
    [Fact]
    public async Task Callback_confirms_delivery_and_cannot_regress_to_queued_or_failed()
    {
        await using var db = CreateDb();
        var delivery = new NotificationDelivery { UserId = Guid.NewGuid(), ProviderMessageId = "SMtest", Status = NotificationDeliveryStatuses.Submitted };
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        var service = new NotificationStatusService(db);
        Assert.True(await service.ApplyAsync(delivery.Id, "SMtest", "delivered", null));
        Assert.Equal(NotificationDeliveryStatuses.Delivered, delivery.Status);
        Assert.NotNull(delivery.SentAt);
        await service.ApplyAsync(delivery.Id, "SMtest", "queued", null);
        await service.ApplyAsync(delivery.Id, "SMtest", "undelivered", "63016");
        Assert.Equal(NotificationDeliveryStatuses.Delivered, delivery.Status);
        await service.ApplyAsync(delivery.Id, "SMtest", "read", null);
        Assert.Equal(NotificationDeliveryStatuses.Read, delivery.Status);
    }

    [Fact]
    public async Task Callback_can_arrive_before_send_response_but_cannot_replace_an_existing_message_sid()
    {
        await using var db = CreateDb();
        var delivery = new NotificationDelivery { UserId = Guid.NewGuid() };
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        var service = new NotificationStatusService(db);
        Assert.True(await service.ApplyAsync(delivery.Id, "SMone", "sent", null));
        Assert.False(await service.ApplyAsync(delivery.Id, "SMother", "delivered", null));
        Assert.Equal("SMone", delivery.ProviderMessageId);
    }

    [Fact]
    public async Task Confirmed_provider_failure_is_recorded_without_automatic_resubmission()
    {
        await using var db = CreateDb();
        var delivery = new NotificationDelivery { UserId = Guid.NewGuid(), Status = NotificationDeliveryStatuses.Submitted };
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        await new NotificationStatusService(db).ApplyAsync(delivery.Id, "SMtest", "undelivered", "63016");
        Assert.Equal(NotificationDeliveryStatuses.Failed, delivery.Status);
        Assert.Equal("63016", delivery.ErrorMessage);
        Assert.Null(delivery.NextAttemptAt);
    }

    private static LumaDbContext CreateDb() => new(new DbContextOptionsBuilder<LumaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
