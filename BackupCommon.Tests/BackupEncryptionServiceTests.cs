using System;
using System.IO;
using System.Text;
using SecureServerBackupCommon;
using Xunit;

namespace SecureServerBackupCommon.Tests;

public sealed class BackupEncryptionServiceTests : IDisposable
{
    private readonly string _rootPath;

    public BackupEncryptionServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "SecureServerBackupTests", nameof(BackupEncryptionServiceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
    }

    [Fact]
    public void ProtectPassword_WhenPasswordIsBlank_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => BackupEncryptionService.ProtectPassword(" "));
    }

    [Fact]
    public void ProtectPassword_RoundTripsWithUnprotectPassword()
    {
        const string password = "P@ssword!123";

        string protectedPassword = BackupEncryptionService.ProtectPassword(password);
        string unprotectedPassword = BackupEncryptionService.UnprotectPassword(protectedPassword);

        Assert.Equal(password, unprotectedPassword);
    }

    [Fact]
    public void EncryptFile_ThenDecryptFile_RestoresOriginalContent()
    {
        string inputPath = CreateFile("plain.ssb", "backup-content");
        string encryptedPath = Path.Combine(_rootPath, "encrypted.ssb");
        string decryptedPath = Path.Combine(_rootPath, "decrypted.ssb");

        BackupEncryptionService.EncryptFile(inputPath, encryptedPath, "StrongPassword!");
        BackupEncryptionService.DecryptFile(encryptedPath, decryptedPath, "StrongPassword!");

        Assert.Equal("backup-content", File.ReadAllText(decryptedPath));
    }

    [Fact]
    public void IsEncryptedBackupFile_WhenFileWasEncrypted_ReturnsTrue()
    {
        string inputPath = CreateFile("plain.ssb", "backup-content");
        string encryptedPath = Path.Combine(_rootPath, "encrypted.ssb");

        BackupEncryptionService.EncryptFile(inputPath, encryptedPath, "StrongPassword!");

        Assert.True(BackupEncryptionService.IsEncryptedBackupFile(encryptedPath));
    }

    [Fact]
    public void DecryptFile_WhenPasswordIsWrong_ThrowsInvalidOperationExceptionAndDoesNotLeaveOutput()
    {
        string inputPath = CreateFile("plain.ssb", "backup-content");
        string encryptedPath = Path.Combine(_rootPath, "encrypted.ssb");
        string decryptedPath = Path.Combine(_rootPath, "decrypted.ssb");

        BackupEncryptionService.EncryptFile(inputPath, encryptedPath, "StrongPassword!");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            BackupEncryptionService.DecryptFile(encryptedPath, decryptedPath, "WrongPassword!"));

        Assert.Contains("Invalid encryption password", exception.Message);
        Assert.False(File.Exists(decryptedPath));
    }

    [Fact]
    public void CreateTemporaryBackupPath_SanitizesInvalidCharactersAndUsesSsbExtension()
    {
        string tempPath = BackupEncryptionService.CreateTemporaryBackupPath("bad:name?.ssb");

        Assert.EndsWith(".ssb", tempPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(':', Path.GetFileName(tempPath));
        Assert.DoesNotContain('?', Path.GetFileName(tempPath));

        BackupEncryptionService.DeleteTemporaryFile(tempPath);
    }

    [Fact]
    public void DeleteTemporaryFile_WhenFileExists_RemovesFile()
    {
        string tempPath = BackupEncryptionService.CreateTemporaryBackupPath("cleanup-test");
        File.WriteAllText(tempPath, "temporary");

        BackupEncryptionService.DeleteTemporaryFile(tempPath);

        Assert.False(File.Exists(tempPath));
    }

    private string CreateFile(string fileName, string content)
    {
        string path = Path.Combine(_rootPath, fileName);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}
