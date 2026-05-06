// Модель UI/отправки BitCanary: «ChatSendPayload».
using Avalonia.Platform.Storage;

namespace Messenger.Client.Avalonia.Models;

public sealed record ChatSendPayload(string Text, IStorageFile? ImageAttachment);
