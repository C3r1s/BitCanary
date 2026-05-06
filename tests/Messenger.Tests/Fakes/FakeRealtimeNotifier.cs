// Автотест BitCanary: проверка «FakeRealtimeNotifier».
using Messenger.Application.Abstractions;
using NSubstitute;

namespace Messenger.Tests.Fakes;

public static class FakeRealtimeNotifier
{
    public static IRealtimeNotifier Create() => Substitute.For<IRealtimeNotifier>();
}
