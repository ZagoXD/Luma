using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public interface IUserImageDeletion
{
    Task DeleteAsync(Guid userId, CancellationToken ct);
}

public sealed class R2UserDataDeletion(IOptions<R2Options> options) : IUserImageDeletion
{
    public async Task DeleteAsync(Guid userId, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.AccessKeyId) || string.IsNullOrWhiteSpace(settings.Endpoint)) return;
        using var client = new AmazonS3Client(new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey),
            new AmazonS3Config { ServiceURL = settings.Endpoint, ForcePathStyle = true, AuthenticationRegion = "auto" });
        var prefix = settings.BabyImagePrefix.Trim('/') + "/" + userId.ToString("N") + "/";
        while (true)
        {
            var objects = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = settings.BucketName, Prefix = prefix }, ct);
            if (objects.S3Objects.Count == 0) return;
            var response = await client.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = settings.BucketName,
                Objects = objects.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList(), Quiet = true }, ct);
            if (response.DeleteErrors.Count > 0) throw new HttpRequestException("A exclusão de imagens temporárias aguarda nova tentativa.");
        }
    }
}
