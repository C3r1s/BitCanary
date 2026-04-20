using Messenger.Application.Abstractions;
using NSubstitute;

namespace Messenger.Tests.Fakes;

/// <summary>
/// Per D-03: NSubstitute stub factory for IRealtimeNotifier.
/// NSubstitute auto-implements all 7 async methods returning Task.CompletedTask.
/// </summary>
public static class FakeRealtimeNotifier
{
    public static IRealtimeNotifier Create() => Substitute.For<IRealtimeNotifier>();
}
