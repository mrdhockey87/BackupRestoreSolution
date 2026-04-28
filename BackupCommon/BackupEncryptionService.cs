using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SecureServerBackupCommon
{
    public static class BackupEncryptionService
    {
        private static readonly byte[] MagicHeader = Encoding.ASCII.GetBytes("SSBAES1");
        private static readonly byte[] PasswordEntropy = Encoding.UTF8.GetBytes("BackupRestoreSolution::EncryptionPassword");

        public static string ProtectPassword(string plainTextPassword)
        {
            if (string.IsNullOrWhiteSpace(plainTextPassword))
            {
                throw new ArgumentException("Password cannot be empty.", nameof(plainTextPassword));
            }

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainTextPassword);
            byte[] protectedBytes = ProtectedData.Protect(plainBytes, PasswordEntropy, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string UnprotectPassword(string protectedPassword)
        {
            if (string.IsNullOrWhiteSpace(protectedPassword))
            {
                throw new ArgumentException("Protected password is missing.", nameof(protectedPassword));
            }

            try
            {
                byte[] protectedBytes = Convert.FromBase64String(protectedPassword);
                byte[] plainBytes = ProtectedData.Unprotect(protectedBytes, PasswordEntropy, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to decrypt the stored backup password.", ex);
            }
        }

        public static bool IsEncryptedBackupFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            using var stream = File.OpenRead(filePath);
            if (stream.Length < MagicHeader.Length + 32)
            {
                return false;
            }

            byte[] header = new byte[MagicHeader.Length];
            int bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead != header.Length)
            {
                return false;
            }

            return header.AsSpan().SequenceEqual(MagicHeader);
        }

        public static string CreateTemporaryBackupPath(string backupNameHint)
        {
            string sanitizedName = string.IsNullOrWhiteSpace(backupNameHint)
                ? "backup"
                : string.Concat(backupNameHint.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            string tempDirectory = Path.Combine(Path.GetTempPath(), "BackupRestoreApp", "DecryptedBackups");
            Directory.CreateDirectory(tempDirectory);
            return Path.Combine(tempDirectory, $"{sanitizedName}_{Guid.NewGuid():N}.ssb");
        }

        public static void EncryptFile(string inputPath, string outputPath, string password)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("Backup file to encrypt was not found.", inputPath);
            }

            string tempOutputPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");
            Directory.CreateDirectory(Path.GetDirectoryName(tempOutputPath)!);

            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] iv = RandomNumberGenerator.GetBytes(16);

            using (var inputStream = File.OpenRead(inputPath))
            using (var outputStream = File.Create(tempOutputPath))
            {
                outputStream.Write(MagicHeader, 0, MagicHeader.Length);
                outputStream.Write(salt, 0, salt.Length);
                outputStream.Write(iv, 0, iv.Length);

                using var aes = Aes.Create();
                aes.KeySize = 128;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = DeriveKey(password, salt);
                aes.IV = iv;

                using var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
                inputStream.CopyTo(cryptoStream);
                cryptoStream.FlushFinalBlock();
            }

            ReplaceFileAtomically(tempOutputPath, outputPath);
        }

        public static void DecryptFile(string encryptedPath, string outputPath, string password)
        {
            if (!File.Exists(encryptedPath))
            {
                throw new FileNotFoundException("Encrypted backup file was not found.", encryptedPath);
            }

            string tempOutputPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");
            Directory.CreateDirectory(Path.GetDirectoryName(tempOutputPath)!);

            try
            {
                using var inputStream = File.OpenRead(encryptedPath);
                using var outputStream = File.Create(tempOutputPath);

                byte[] magic = ReadExact(inputStream, MagicHeader.Length);
                if (!magic.AsSpan().SequenceEqual(MagicHeader))
                {
                    throw new InvalidOperationException("The selected file is not an encrypted backup created by this application.");
                }

                byte[] salt = ReadExact(inputStream, 16);
                byte[] iv = ReadExact(inputStream, 16);

                using var aes = Aes.Create();
                aes.KeySize = 128;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = DeriveKey(password, salt);
                aes.IV = iv;

                try
                {
                    using var cryptoStream = new CryptoStream(inputStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
                    cryptoStream.CopyTo(outputStream);
                }
                catch (CryptographicException ex)
                {
                    throw new InvalidOperationException("Invalid encryption password or corrupted encrypted backup.", ex);
                }
            }
            catch
            {
                DeleteTemporaryFile(tempOutputPath);
                throw;
            }

            ReplaceFileAtomically(tempOutputPath, outputPath);
        }

        public static string DecryptFileToTemporaryLocation(string encryptedPath, string password)
        {
            string tempPath = CreateTemporaryBackupPath(Path.GetFileNameWithoutExtension(encryptedPath));
            DecryptFile(encryptedPath, tempPath, password);
            return tempPath;
        }

        public static void DeleteTemporaryFile(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                if (File.Exists(filePath))
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                    File.Delete(filePath);
                }

                DeleteEmptyTemporaryDirectory(filePath);
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Temporary backup cleanup failed for '{filePath}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Temporary backup cleanup failed for '{filePath}': {ex.Message}");
            }
        }

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            using var deriveBytes = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
            return deriveBytes.GetBytes(16);
        }

        private static void ReplaceFileAtomically(string tempOutputPath, string outputPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tempOutputPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            string? outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            if (File.Exists(outputPath))
            {
                File.Replace(tempOutputPath, outputPath, null, true);
            }
            else
            {
                File.Move(tempOutputPath, outputPath);
            }
        }

        private static void DeleteEmptyTemporaryDirectory(string filePath)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "BackupRestoreApp", "DecryptedBackups");
            string? directoryPath = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directoryPath) || !directoryPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Directory.Exists(directoryPath)
                && Directory.GetFiles(directoryPath).Length == 0
                && Directory.GetDirectories(directoryPath).Length == 0)
            {
                Directory.Delete(directoryPath);
            }

            if (Directory.Exists(tempRoot)
                && Directory.GetFiles(tempRoot).Length == 0
                && Directory.GetDirectories(tempRoot).Length == 0)
            {
                Directory.Delete(tempRoot);
            }
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int bytesRead = stream.Read(buffer, offset, count - offset);
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException("Unexpected end of encrypted backup file.");
                }

                offset += bytesRead;
            }

            return buffer;
        }
    }
}
