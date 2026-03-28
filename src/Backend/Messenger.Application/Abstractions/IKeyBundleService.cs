using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Application.Abstractions;

public interface IKeyBundleService
{
    Task<BundleUploadResponse> UploadBundleAsync(Guid userId, KeyBundleUploadRequest request, CancellationToken cancellationToken);
    Task<KeyBundleDto?> GetBundleAsync(Guid userId, CancellationToken cancellationToken);
    Task<OtpkReplenishResponse> ReplenishOpksAsync(Guid userId, OtpkReplenishRequest request, CancellationToken cancellationToken);
}
