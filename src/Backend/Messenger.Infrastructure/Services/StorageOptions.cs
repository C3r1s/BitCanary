// Параметры каталога хранения медиафайлов BitCanary.
namespace Messenger.Infrastructure.Services;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string RootPath { get; set; } = "storage";
}
