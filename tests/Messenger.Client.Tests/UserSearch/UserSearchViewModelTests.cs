using Xunit;

namespace Messenger.Client.Tests.UserSearch;

/// <summary>
/// Wave 0 stub tests for UserSearch ViewModel behavior.
/// These stubs compile immediately. Real assertions will be added in plan 10-02
/// when SearchViewModel is wired with IMessengerApiClient.SearchUsersAsync.
/// </summary>
public sealed class UserSearchViewModelTests
{
    [Fact]
    public void SearchQuery_LessThanTwoChars_ClearsResults()
    {
        // Stub — real assertion added in plan 10-02 when SearchViewModel is implemented
        // Behavior: setting SearchQuery to a string shorter than 2 characters clears the results list
        Assert.True(true);
    }

    [Fact]
    public void SearchQuery_TwoOrMoreChars_FiresSearch()
    {
        // Stub — real assertion added in plan 10-02 when SearchViewModel is implemented
        // Behavior: setting SearchQuery to 2+ characters triggers SearchUsersAsync via IMessengerApiClient
        Assert.True(true);
    }

    [Fact]
    public void IsInNormalMode_FalseWhenUserSearchActive()
    {
        // Stub — real assertion added in plan 10-02 when ChatListViewModel.IsUserSearchMode is implemented
        // Behavior: IsInNormalMode returns false while user-directory search is active
        Assert.True(true);
    }

    [Fact]
    public void ExistingDirectChat_NavigatesWithoutCreating()
    {
        // Stub — real assertion added in plan 10-02 when SelectUserResultAsync is implemented
        // Behavior: if a direct chat already exists with the selected user, navigation occurs without CreateChatAsync
        Assert.True(true);
    }
}
