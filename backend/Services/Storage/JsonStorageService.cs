using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JYCareerAgent.Api.Services.Storage;

public class JsonStorageService : IJsonStorageService
{
    private readonly string _basePath;
    private readonly string _backupPath;
    private readonly ILogger<JsonStorageService> _logger;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonStorageService(IConfiguration configuration, ILogger<JsonStorageService> logger)
    {
        _logger = logger;
        
        // Resolve data directory relative to project root or configured path
        var configuredRoot = configuration["JY_STORAGE_ROOT"];
        var root = !string.IsNullOrWhiteSpace(configuredRoot) 
            ? configuredRoot 
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        if (!Directory.Exists(Path.Combine(root, "data")) && Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "data")))
        {
            root = Directory.GetCurrentDirectory();
        }
        else if (!Directory.Exists(Path.Combine(root, "data")))
        {
            // If running inside backend folder or build output
            var parent = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName;
            if (parent != null && Directory.Exists(Path.Combine(parent, "data")))
            {
                root = parent;
            }
        }

        _basePath = root;
        _backupPath = Path.Combine(_basePath, "backups");
        
        Directory.CreateDirectory(Path.Combine(_basePath, "data"));
        Directory.CreateDirectory(_backupPath);
        
        _logger.LogInformation("JsonStorageService initialized. Root: {Root}, Backups: {Backups}", _basePath, _backupPath);
    }

    private SemaphoreSlim GetLock(string normalizedPath)
    {
        return Locks.GetOrAdd(normalizedPath, _ => new SemaphoreSlim(1, 1));
    }

    private string ResolveFullPath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(_basePath, relativePath));
    }

    public bool FileExists(string relativePath)
    {
        return File.Exists(ResolveFullPath(relativePath));
    }

    public long GetFileSize(string relativePath)
    {
        var fullPath = ResolveFullPath(relativePath);
        if (!File.Exists(fullPath)) return 0;
        return new FileInfo(fullPath).Length;
    }

    public async Task<T?> LoadAsync<T>(string relativePath, CancellationToken cancellationToken = default) where T : class
    {
        var fullPath = ResolveFullPath(relativePath);
        var fileLock = GetLock(fullPath);

        await fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("JSON file not found at {Path}. Returning null.", fullPath);
                return null;
            }

            var size = new FileInfo(fullPath).Length;
            if (size > 10 * 1024 * 1024)
            {
                _logger.LogWarning("Performance Alert: JSON file {Path} size is {SizeMB:F2} MB.", fullPath, size / (1024.0 * 1024.0));
            }

            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON Deserialization failed for {Path}. File might be corrupted.", fullPath);
            throw new InvalidOperationException($"Corrupted JSON data in {relativePath}: {ex.Message}", ex);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task SaveAsync<T>(string relativePath, T data, bool createBackup = false, CancellationToken cancellationToken = default)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var fullPath = ResolveFullPath(relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var fileLock = GetLock(fullPath);
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            // If backup is requested or file already exists and is significant
            if (createBackup && File.Exists(fullPath))
            {
                await CreateBackupInternalAsync(fullPath, relativePath, cancellationToken);
            }

            // Safe Atomic Write Pattern:
            // 1. Serialize to memory / temp file in same directory
            // 2. Flush and close temp file
            // 3. Atomically move/replace target file
            var tempFile = $"{fullPath}.tmp.{Guid.NewGuid():N}";
            await using (var tempStream = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(tempStream, data, JsonOptions, cancellationToken);
                await tempStream.FlushAsync(cancellationToken);
            }

            // Atomic replacement
            File.Move(tempFile, fullPath, overwrite: true);

            var writtenSize = new FileInfo(fullPath).Length;
            if (writtenSize > 10 * 1024 * 1024)
            {
                _logger.LogWarning("File size alert: {Path} grew to {SizeMB:F2} MB.", fullPath, writtenSize / (1024.0 * 1024.0));
            }

            _logger.LogDebug("Saved JSON data to {Path} ({Bytes} bytes).", fullPath, writtenSize);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task<string> CreateBackupAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(relativePath);
        var fileLock = GetLock(fullPath);
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            return await CreateBackupInternalAsync(fullPath, relativePath, cancellationToken);
        }
        finally
        {
            fileLock.Release();
        }
    }

    private async Task<string> CreateBackupInternalAsync(string fullPath, string relativePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Cannot backup non-existent file: {fullPath}");

        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(relativePath);
        var ext = Path.GetExtension(relativePath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var backupFileName = $"{fileNameWithoutExt}_{timestamp}{ext}";
        var destination = Path.Combine(_backupPath, backupFileName);

        await using (var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        await using (var dest = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await source.CopyToAsync(dest, cancellationToken);
            await dest.FlushAsync(cancellationToken);
        }

        _logger.LogInformation("Backup created: {BackupFileName} for {RelativePath}", backupFileName, relativePath);
        RotateBackups(fileNameWithoutExt, 30);
        return backupFileName;
    }

    public async Task<bool> RestoreBackupAsync(string relativePath, string backupFileName, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(relativePath);
        var backupFullPath = Path.Combine(_backupPath, backupFileName);

        if (!File.Exists(backupFullPath))
        {
            _logger.LogError("Backup file not found: {BackupPath}", backupFullPath);
            return false;
        }

        var fileLock = GetLock(fullPath);
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            // Verify backup content is valid JSON before restoring
            await using (var checkStream = new FileStream(backupFullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                using var doc = await JsonDocument.ParseAsync(checkStream, cancellationToken: cancellationToken);
            }

            // Create recovery backup of current file if it exists
            if (File.Exists(fullPath))
            {
                await CreateBackupInternalAsync(fullPath, $"{relativePath}.pre_restore", cancellationToken);
            }

            File.Copy(backupFullPath, fullPath, overwrite: true);
            _logger.LogInformation("Restored {Path} from backup {BackupFileName}", fullPath, backupFileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore backup {BackupFileName} to {Path}", backupFileName, fullPath);
            return false;
        }
        finally
        {
            fileLock.Release();
        }
    }

    public Task<List<string>> ListBackupsAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var baseName = Path.GetFileNameWithoutExtension(relativePath);
        if (!Directory.Exists(_backupPath))
            return Task.FromResult(new List<string>());

        var pattern = $"{baseName}_*";
        var files = Directory.GetFiles(_backupPath, pattern)
            .Select(Path.GetFileName)
            .Where(f => !string.IsNullOrEmpty(f))
            .Select(f => f!)
            .OrderByDescending(f => f)
            .ToList();

        return Task.FromResult(files);
    }

    private void RotateBackups(string filePrefix, int maxToKeep)
    {
        try
        {
            var files = Directory.GetFiles(_backupPath, $"{filePrefix}_*.json")
                .OrderByDescending(File.GetCreationTimeUtc)
                .Skip(maxToKeep)
                .ToList();

            foreach (var oldFile in files)
            {
                File.Delete(oldFile);
                _logger.LogDebug("Rotated and removed old backup: {OldFile}", oldFile);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up old backups for {Prefix}", filePrefix);
        }
    }
}
