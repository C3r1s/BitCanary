// DTO передачи «KeyBundleContracts» между API BitCanary и клиентами.
namespace Messenger.Shared.Contracts.Dtos;

public sealed record KeyBundleUploadRequest(
    Guid? DeviceId,
    byte[] IkPublic,
    byte[] SpkPublic,
    byte[] SpkSignature);

public sealed record KeyBundleDto(
    Guid UserId,
    Guid DeviceId,
    byte[] IkPublic,
    byte[] SpkPublic,
    byte[] SpkSignature,
    DateTimeOffset SpkCreatedAt,
    byte[]? OpkPublic,
    Guid? OpkId);

public sealed record OtpkReplenishRequest(
    Guid DeviceId,
    byte[][] PreKeys);

public sealed record BundleUploadResponse(
    Guid DeviceId);

public sealed record OtpkReplenishResponse(Guid[] AssignedIds);
