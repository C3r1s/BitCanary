// Модель UI/отправки BitCanary: «TerminalSchemeOption».
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.Models;

public sealed record TerminalSchemeOption(TerminalColorScheme Value, string Label);
