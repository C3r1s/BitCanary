// Автотест BitCanary: проверка «SqliteSchemaTests».
using Messenger.Client.Avalonia.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Messenger.Client.Tests.Storage;

[Trait("Category", "Unit")]
public sealed class SqliteSchemaTests
{
    private static (DatabaseService Service, string TempDir) BuildService()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var provider = new ServiceCollection()
            .AddDataProtection()
            .SetApplicationName("Messenger.Test")
            .PersistKeysToFileSystem(new DirectoryInfo(tempDir))
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();
        return (new DatabaseService(provider), tempDir);
    }

    [Fact]
    public async Task AllTablesExist()
    {
        var (svc, tempDir) = BuildService();

        await using var conn = await svc.OpenAsync(localAppDataOverride: tempDir);

        var tables = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("chats", tables);
        Assert.Contains("messages", tables);
        Assert.Contains("ratchet_sessions", tables);
        Assert.Contains("skipped_message_keys", tables);
        Assert.Contains("schema_migrations", tables);
    }

    [Fact]
    public async Task WalModeEnabled()
    {
        var (svc, tempDir) = BuildService();

        await using var conn = await svc.OpenAsync(localAppDataOverride: tempDir);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var result = (string)(await cmd.ExecuteScalarAsync())!;

        Assert.Equal("wal", result);
    }

    [Fact]
    public async Task SchemaMigrationsVersion1()
    {
        var (svc, tempDir) = BuildService();

        await using var conn = await svc.OpenAsync(localAppDataOverride: tempDir);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_migrations WHERE version=1;";
        var result = await cmd.ExecuteScalarAsync();

        Assert.NotNull(result);
        Assert.Equal(1L, result);
    }

    [Fact]
    public async Task DbKeyProtection()
    {
        var (svc, tempDir) = BuildService();

        await using var conn = await svc.OpenAsync(localAppDataOverride: tempDir);

        var keyFile = Path.Combine(tempDir, "db-key.bin");

        Assert.True(File.Exists(keyFile), "db-key.bin should exist after first OpenAsync");

        var bytes = await File.ReadAllBytesAsync(keyFile);
        Assert.True(bytes.Length > 0, "db-key.bin should not be empty");
        Assert.NotEqual(0x7B, bytes[0]);
        if (bytes.Length > 1)
        {
            Assert.False(bytes[0] == 0x1F && bytes[1] == 0x8B, "db-key.bin should not be gzip-encoded");
        }
    }

    [Fact]
    public async Task FtsVirtualTableExists()
    {
        var (svc, tempDir) = BuildService();

        await using var conn = await svc.OpenAsync(localAppDataOverride: tempDir);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='messages_fts';";
        var result = await cmd.ExecuteScalarAsync();

        Assert.NotNull(result);
        Assert.Equal("messages_fts", result);
    }

    [Fact]
    public async Task FtsTriggersExist()
    {
        var (svc, tempDir) = BuildService();

        await using var conn = await svc.OpenAsync(localAppDataOverride: tempDir);

        var triggers = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger';";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            triggers.Add(reader.GetString(0));
        }

        Assert.Contains("messages_ai", triggers);
        Assert.Contains("messages_ad", triggers);
        Assert.Contains("messages_au", triggers);
    }

    [Fact]
    public async Task SchemaMigrationsVersion3()
    {
        var (svc, tempDir) = BuildService();

        await using var conn = await svc.OpenAsync(localAppDataOverride: tempDir);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_migrations WHERE version=3;";
        var result = await cmd.ExecuteScalarAsync();

        Assert.NotNull(result);
        Assert.Equal(3L, result);
    }
}
