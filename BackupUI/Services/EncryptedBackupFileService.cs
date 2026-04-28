using System;
using System.IO;
using System.Windows;
using SecureServerBackupCommon;
using SecureServerBackup.Windows;

namespace SecureServerBackup.Services
{
    public sealed class PreparedBackupFile : IDisposable
    {
        private bool _disposed;

        public string OriginalPath { get; init; } = string.Empty;
        public string WorkingPath { get; init; } = string.Empty;
        public bool IsTemporary { get; init; }

        ~PreparedBackupFile()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (IsTemporary)
            {
                BackupEncryptionService.DeleteTemporaryFile(WorkingPath);
            }

            _disposed = true;
        }
    }

    public static class EncryptedBackupFileService
    {
        public static PreparedBackupFile PrepareForRead(Window? owner, string backupPath, string backupName, string? protectedPassword = null)
        {
            if (!BackupEncryptionService.IsEncryptedBackupFile(backupPath))
            {
                return new PreparedBackupFile
                {
                    OriginalPath = backupPath,
                    WorkingPath = backupPath,
                    IsTemporary = false
                };
            }

            string? promptError = null;
            bool allowStoredPassword = !string.IsNullOrWhiteSpace(protectedPassword);

            while (true)
            {
                try
                {
                    string password;
                    if (allowStoredPassword)
                    {
                        password = BackupEncryptionService.UnprotectPassword(protectedPassword!);
                        allowStoredPassword = false;
                    }
                    else
                    {
                        var prompt = new BackupPasswordPromptWindow(backupName);
                        if (owner != null)
                        {
                            prompt.Owner = owner;
                        }

                        if (!string.IsNullOrWhiteSpace(promptError))
                        {
                            prompt.SetError(promptError);
                        }

                        if (prompt.ShowDialog() != true)
                        {
                            throw new OperationCanceledException("The encrypted backup password prompt was cancelled.");
                        }

                        password = prompt.EnteredPassword;
                    }

                    string tempPath = BackupEncryptionService.DecryptFileToTemporaryLocation(backupPath, password);
                    return new PreparedBackupFile
                    {
                        OriginalPath = backupPath,
                        WorkingPath = tempPath,
                        IsTemporary = true
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    promptError = ex.Message;
                    if (owner == null)
                    {
                        throw;
                    }
                }
            }
        }
    }
}
