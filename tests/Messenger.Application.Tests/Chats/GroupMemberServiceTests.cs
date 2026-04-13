using System.Net;
using Messenger.Application.Abstractions;
using Messenger.Application.Chats;
using Messenger.Application.Common;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Persistence;
using Messenger.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Messenger.Application.Tests.Chats;

public sealed class GroupMemberServiceTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static ICurrentUserContext CreateCurrentUser(Guid userId)
    {
        var ctx = Substitute.For<ICurrentUserContext>();
        ctx.RequireUserId().Returns(userId);
        return ctx;
    }

    private static ChatService CreateService(AppDbContext db, ICurrentUserContext currentUser)
        => new(db, currentUser);

    private static (AppDbContext db, Guid chatId, Guid ownerId, Guid adminId, Guid memberId) SeedGroupChat()
    {
        var db = CreateDb();
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var owner = new User { Id = ownerId, UserName = "owner", DisplayName = "Owner", PasswordHash = "x", PublicKey = "k" };
        var admin = new User { Id = adminId, UserName = "admin", DisplayName = "Admin", PasswordHash = "x", PublicKey = "k" };
        var member = new User { Id = memberId, UserName = "member", DisplayName = "Member", PasswordHash = "x", PublicKey = "k" };
        db.Users.AddRange(owner, admin, member);

        var chat = new Chat { Title = "Test Group", Type = ChatType.Group };
        db.Chats.Add(chat);
        db.SaveChanges();

        var chatId = chat.Id;
        db.ChatMemberships.AddRange(
            new ChatMembership { ChatId = chatId, UserId = ownerId, Role = ChatRole.Owner },
            new ChatMembership { ChatId = chatId, UserId = adminId, Role = ChatRole.Admin },
            new ChatMembership { ChatId = chatId, UserId = memberId, Role = ChatRole.Member }
        );
        db.SaveChanges();

        return (db, chatId, ownerId, adminId, memberId);
    }

    [Fact]
    public async Task AddMember_MemberRole_ThrowsForbidden()
    {
        var (db, chatId, _, _, memberId) = SeedGroupChat();
        var newUser = new User { Id = Guid.NewGuid(), UserName = "newuser", DisplayName = "New", PasswordHash = "x", PublicKey = "k" };
        db.Users.Add(newUser);
        await db.SaveChangesAsync();

        var svc = CreateService(db, CreateCurrentUser(memberId));
        var ex = await Assert.ThrowsAsync<AppException>(
            () => svc.AddMemberAsync(chatId, newUser.Id, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task AddMember_AlreadyMember_ThrowsConflict()
    {
        var (db, chatId, _, adminId, memberId) = SeedGroupChat();
        var svc = CreateService(db, CreateCurrentUser(adminId));

        var ex = await Assert.ThrowsAsync<AppException>(
            () => svc.AddMemberAsync(chatId, memberId, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_AdminRemovesAdmin_ThrowsForbidden()
    {
        var (db, chatId, _, adminId, _) = SeedGroupChat();

        // Add a second admin to remove
        var secondAdminId = Guid.NewGuid();
        var secondAdmin = new User { Id = secondAdminId, UserName = "admin2", DisplayName = "Admin2", PasswordHash = "x", PublicKey = "k" };
        db.Users.Add(secondAdmin);
        db.ChatMemberships.Add(new ChatMembership { ChatId = chatId, UserId = secondAdminId, Role = ChatRole.Admin });
        await db.SaveChangesAsync();

        var svc = CreateService(db, CreateCurrentUser(adminId));
        var ex = await Assert.ThrowsAsync<AppException>(
            () => svc.RemoveMemberAsync(chatId, secondAdminId, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_MemberSelfLeave_Succeeds()
    {
        var (db, chatId, _, _, memberId) = SeedGroupChat();
        var svc = CreateService(db, CreateCurrentUser(memberId));

        await svc.RemoveMemberAsync(chatId, memberId, CancellationToken.None);

        var stillMember = await db.ChatMemberships.AnyAsync(x => x.ChatId == chatId && x.UserId == memberId);
        Assert.False(stillMember);
    }

    [Fact]
    public async Task RemoveMember_OwnerSelfLeave_ThrowsBadRequest()
    {
        var (db, chatId, ownerId, _, _) = SeedGroupChat();
        var svc = CreateService(db, CreateCurrentUser(ownerId));

        var ex = await Assert.ThrowsAsync<AppException>(
            () => svc.RemoveMemberAsync(chatId, ownerId, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateMemberRole_AdminCaller_ThrowsForbidden()
    {
        var (db, chatId, _, adminId, memberId) = SeedGroupChat();
        var svc = CreateService(db, CreateCurrentUser(adminId));

        var ex = await Assert.ThrowsAsync<AppException>(
            () => svc.UpdateMemberRoleAsync(chatId, memberId, ChatRole.Admin, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateMemberRole_AssignOwnerRole_ThrowsBadRequest()
    {
        var (db, chatId, ownerId, _, memberId) = SeedGroupChat();
        var svc = CreateService(db, CreateCurrentUser(ownerId));

        var ex = await Assert.ThrowsAsync<AppException>(
            () => svc.UpdateMemberRoleAsync(chatId, memberId, ChatRole.Owner, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateMemberRole_OwnerDemotesSelf_ThrowsBadRequest()
    {
        var (db, chatId, ownerId, _, _) = SeedGroupChat();
        var svc = CreateService(db, CreateCurrentUser(ownerId));

        var ex = await Assert.ThrowsAsync<AppException>(
            () => svc.UpdateMemberRoleAsync(chatId, ownerId, ChatRole.Admin, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateChat_MemberCaller_ThrowsForbidden()
    {
        var (db, chatId, _, _, memberId) = SeedGroupChat();
        var svc = CreateService(db, CreateCurrentUser(memberId));

        var ex = await Assert.ThrowsAsync<AppException>(
            () => svc.UpdateChatAsync(chatId, new UpdateChatRequest("New Title", null), CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }
}
