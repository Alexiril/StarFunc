using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace StarFunc.Infrastructure
{
    /// <summary>
    /// Manages JWT access/refresh tokens (API.md §2, §9.2).
    /// Access token: in-memory only (1 hour TTL).
    /// Refresh token: encrypted at rest with AES-256 (key = deviceId + hardware fingerprint).
    /// </summary>
    public class TokenManager
    {
        const string RefreshTokenFileName = "rt.bin";
        const int KeySizeBytes = 32; // AES-256
        const int IvSizeBytes = 16;

        string _accessToken;
        DateTime _accessTokenExpiry;

        readonly string _refreshTokenPath;
        readonly byte[] _encryptionKey;

        public TokenManager()
        {
            _refreshTokenPath = Path.Combine(Application.persistentDataPath, RefreshTokenFileName);
            _encryptionKey = DeriveKey();
        }

        /// <summary>
        /// Returns the current access token if still valid, or null.
        /// </summary>
        public string GetAccessToken()
        {
            if (string.IsNullOrEmpty(_accessToken))
                return null;

            if (DateTime.UtcNow >= _accessTokenExpiry)
            {
                _accessToken = null;
                return null;
            }

            return _accessToken;
        }

        /// <summary>
        /// Store tokens after successful authentication.
        /// </summary>
        /// <param name="accessToken">JWT access token.</param>
        /// <param name="refreshToken">JWT refresh token (will be encrypted to disk).</param>
        /// <param name="expiresInSeconds">Access token TTL in seconds (default 3600).</param>
        public void SetTokens(string accessToken, string refreshToken, int expiresInSeconds = 3600)
        {
            _accessToken = accessToken;
            // Shave off 60 seconds to avoid edge-case expiry during a request
            _accessTokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(expiresInSeconds - 60, 0));

            SaveRefreshToken(refreshToken);
        }

        /// <summary>
        /// Read the stored (encrypted) refresh token, or null if none exists.
        /// </summary>
        public string GetRefreshToken()
        {
            return LoadRefreshToken();
        }

        /// <summary>
        /// Clear all tokens (logout / auth failure).
        /// </summary>
        public void ClearTokens()
        {
            _accessToken = null;
            _accessTokenExpiry = DateTime.MinValue;

            try
            {
                if (File.Exists(_refreshTokenPath))
                    File.Delete(_refreshTokenPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TokenManager] Failed to delete refresh token file: {e.Message}");
            }
        }

        #region Encryption

        void SaveRefreshToken(string refreshToken)
        {
            try
            {
                byte[] plaintext = Encoding.UTF8.GetBytes(refreshToken);

                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Key = _encryptionKey;
                aes.GenerateIV();

                using var encryptor = aes.CreateEncryptor();
                byte[] ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

                // File format: [16-byte IV][ciphertext]
                using var fs = new FileStream(_refreshTokenPath, FileMode.Create, FileAccess.Write);
                fs.Write(aes.IV, 0, IvSizeBytes);
                fs.Write(ciphertext, 0, ciphertext.Length);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TokenManager] Failed to save refresh token: {e.Message}");
            }
        }

        string LoadRefreshToken()
        {
            try
            {
                if (!File.Exists(_refreshTokenPath))
                    return null;

                byte[] fileBytes = File.ReadAllBytes(_refreshTokenPath);
                if (fileBytes.Length <= IvSizeBytes)
                    return null;

                byte[] iv = new byte[IvSizeBytes];
                Buffer.BlockCopy(fileBytes, 0, iv, 0, IvSizeBytes);

                int cipherLen = fileBytes.Length - IvSizeBytes;
                byte[] ciphertext = new byte[cipherLen];
                Buffer.BlockCopy(fileBytes, IvSizeBytes, ciphertext, 0, cipherLen);

                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Key = _encryptionKey;
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor();
                byte[] plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

                return Encoding.UTF8.GetString(plaintext);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TokenManager] Failed to load refresh token: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Derive a 256-bit key from deviceId + hardware fingerprint using SHA-256.
        /// </summary>
        static byte[] DeriveKey()
        {
            string material = SystemInfo.deviceUniqueIdentifier + "|" + GetHardwareFingerprint();
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(material));
        }

        static string GetHardwareFingerprint()
        {
            // Combine several stable hardware identifiers
            return string.Join("|",
                SystemInfo.deviceModel,
                SystemInfo.graphicsDeviceID.ToString(),
                SystemInfo.processorType);
        }

        #endregion
    }
}
