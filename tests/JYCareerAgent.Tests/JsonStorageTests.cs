using FluentAssertions;
using JYCareerAgent.Api.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JYCareerAgent.Tests;

public class JsonStorageTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonStorageService _storageService;

    public JsonStorageTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "jy_agent_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "JY_STORAGE_ROOT", _testDir }
            })
            .Build();

        _storageService = new JsonStorageService(config, NullLogger<JsonStorageService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    public class TestDataModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    [Fact]
    public async Task SaveAsync_And_LoadAsync_ShouldPersistAndRetrieveDataCorrectly()
    {
        // Arrange
        var testData = new TestDataModel { Id = "test-1", Name = "Senior .NET", Count = 42 };
        var relativePath = "data/test_item.json";

        // Act
        await _storageService.SaveAsync(relativePath, testData, createBackup: false);
        var loaded = await _storageService.LoadAsync<TestDataModel>(relativePath);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("test-1");
        loaded.Name.Should().Be("Senior .NET");
        loaded.Count.Should().Be(42);
    }

    [Fact]
    public async Task SaveAsync_ShouldUseAtomicReplacement_WithoutLeavingTmpFiles()
    {
        // Arrange
        var testData = new TestDataModel { Id = "atomic-1", Name = "Atomic Write Test" };
        var relativePath = "data/atomic.json";

        // Act
        await _storageService.SaveAsync(relativePath, testData);

        // Assert
        _storageService.FileExists(relativePath).Should().BeTrue();
        var filesInDir = Directory.GetFiles(Path.Combine(_testDir, "data"));
        filesInDir.Any(f => f.Contains(".tmp.")).Should().BeFalse("Temporary files should be replaced atomically");
    }

    [Fact]
    public async Task CreateBackupAsync_And_RestoreBackupAsync_ShouldPreserveAndRecoverState()
    {
        // Arrange
        var initial = new TestDataModel { Id = "ver-1", Name = "Original State", Count = 1 };
        var relativePath = "data/backup_test.json";
        await _storageService.SaveAsync(relativePath, initial);

        // Act - Create backup
        var backupName = await _storageService.CreateBackupAsync(relativePath);

        // Mutate original
        var mutated = new TestDataModel { Id = "ver-2", Name = "Mutated State", Count = 99 };
        await _storageService.SaveAsync(relativePath, mutated);

        // Verify mutation
        var loadedMutated = await _storageService.LoadAsync<TestDataModel>(relativePath);
        loadedMutated!.Name.Should().Be("Mutated State");

        // Restore backup
        var restored = await _storageService.RestoreBackupAsync(relativePath, backupName);
        restored.Should().BeTrue();

        // Assert
        var loadedRestored = await _storageService.LoadAsync<TestDataModel>(relativePath);
        loadedRestored!.Name.Should().Be("Original State");
        loadedRestored.Count.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentAccess_ShouldBeSafelyHandledWithoutCorruption()
    {
        // Arrange
        var relativePath = "data/concurrency.json";
        await _storageService.SaveAsync(relativePath, new TestDataModel { Id = "init", Count = 0 });

        // Act - Run 20 concurrent saves
        var tasks = Enumerable.Range(1, 20).Select(i => Task.Run(async () =>
        {
            await _storageService.SaveAsync(relativePath, new TestDataModel { Id = $"worker-{i}", Count = i });
        }));

        await Task.WhenAll(tasks);

        // Assert
        var final = await _storageService.LoadAsync<TestDataModel>(relativePath);
        final.Should().NotBeNull();
        final!.Count.Should().BeInRange(1, 20);
    }

    [Fact]
    public async Task LoadAsync_CorruptedJson_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var relativePath = "data/corrupt.json";
        var fullPath = Path.Combine(_testDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "{ invalid json data :;");

        // Act
        var act = async () => await _storageService.LoadAsync<TestDataModel>(relativePath);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
