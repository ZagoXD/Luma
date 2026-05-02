using System.Threading.Channels;
using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public sealed record BabyImageJob(Guid UserId, string PhoneNumber, int Week, DateTimeOffset RequestedAt);

public interface IBabyImageJobQueue
{
    void Enqueue(BabyImageJob job);
}

public sealed class BabyImageJobQueue : IBabyImageJobQueue
{
    private readonly Channel<BabyImageJob> _channel = Channel.CreateUnbounded<BabyImageJob>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ChannelReader<BabyImageJob> Reader => _channel.Reader;

    public void Enqueue(BabyImageJob job)
    {
        if (!_channel.Writer.TryWrite(job))
        {
            throw new InvalidOperationException("Não foi possível enfileirar a geração de imagem do bebê.");
        }
    }
}

public sealed class BabyImageWorker(
    BabyImageJobQueue queue,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BabyImageWorker> logger) : BackgroundService
{
    private readonly bool _storeMessageBodies = configuration.GetValue("Luma:StoreMessageBodies", false);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Baby image background job failed for user {UserId}.", job.UserId);
            }
        }
    }

    private async Task ProcessAsync(BabyImageJob job, CancellationToken cancellationToken)
    {
        var development = BabyDevelopmentKnowledgeBase.GetByWeek(job.Week);
        if (development is null)
        {
            logger.LogInformation("Baby image job skipped because week {Week} is outside the supported range.", job.Week);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LumaDbContext>();
        var imageService = scope.ServiceProvider.GetRequiredService<IBabyImageService>();
        var sender = scope.ServiceProvider.GetRequiredService<IWhatsAppMediaSender>();

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == job.UserId, cancellationToken);
        if (user is null)
        {
            logger.LogInformation("Baby image job skipped because user {UserId} no longer exists.", job.UserId);
            return;
        }

        var result = await imageService.GenerateAsync(development, job.UserId, cancellationToken);
        if (result.PublicUrl is null)
        {
            logger.LogWarning("Baby image was not generated for user {UserId}: {Message}", job.UserId, result.Message);
            return;
        }

        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? null : user.DisplayName;
        var body = name is null
            ? $"Aqui está a imagem educativa de {development.Week} semanas que eu prometi te enviar. Ela é uma comparação aproximada e não substitui ultrassom, pré-natal ou orientação médica."
            : $"{name}, aqui está a imagem educativa de {development.Week} semanas que eu prometi te enviar. Ela é uma comparação aproximada e não substitui ultrassom, pré-natal ou orientação médica.";

        var sendResult = await sender.SendMediaAsync(job.PhoneNumber, body, result.PublicUrl, cancellationToken);
        if (!sendResult.Success)
        {
            logger.LogWarning("Baby image generated but WhatsApp media send failed for user {UserId}: {Error}", job.UserId, sendResult.ErrorMessage);
            return;
        }

        db.Messages.Add(new ConversationMessage
        {
            UserId = job.UserId,
            Direction = "outbound",
            Provider = "twilio",
            ProviderMessageId = sendResult.ProviderMessageId,
            Body = _storeMessageBodies ? body : null
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
