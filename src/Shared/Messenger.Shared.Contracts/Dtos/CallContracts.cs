// DTO передачи «CallContracts» между API BitCanary и клиентами.
namespace Messenger.Shared.Contracts.Dtos;

public sealed record CallSignalDto(
    Guid ChatId,
    Guid FromUserId,
    Guid ToUserId,
    CallSignalKind Kind,
    string Payload,
    DateTimeOffset SentAtUtc);
