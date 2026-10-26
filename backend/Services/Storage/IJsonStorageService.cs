namespace JYCareerAgent.Api.Services.Storage;

public interface IJsonStorageService
{
    Task<T?> LoadAsync<T>(string relativePath, CancellationToken cancellationToken = default) where T : class;
    Task SaveAsync<T>(string relativePath, T data, bool createBackup = false, CancellationToken cancellationToken = default);
    Task<string> CreateBackupAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<bool> RestoreBackupAsync(string relativePath, string backupFileName, CancellationToken cancellationToken = default);
    Task<List<string>> ListBackupsAsync(string relativePath, CancellationToken cancellationToken = default);
    long GetFileSize(string relativePath);
    bool FileExists(string relativePath);
}
