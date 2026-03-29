using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace StarFunc.Infrastructure
{
    /// <summary>
    /// Authorization service: anonymous device registration, token refresh, account linking.
    /// (API.md §6.1, §10, §11 / Tasks.md §1.13)
    /// </summary>
    public class AuthService
    {
        const string DeviceIdFileName = "did.bin";
        const string PlayerIdFileName = "pid.bin";
        const int AesKeySizeBytes = 32;
        const int AesIvSizeBytes = 16;

        readonly ApiClient _apiClient;
        readonly TokenManager _tokenManager;
        readonly NetworkMonitor _networkMonitor;

        readonly string _deviceIdPath;
        readonly string _playerIdPath;
        readonly byte[] _encryptionKey;

        string _deviceId;
        string _playerId;
        bool _isAuthenticated;

        public string PlayerId => _playerId;
        public string DeviceId => _deviceId;
        public bool IsAuthenticated => _isAuthenticated;

        public AuthService(ApiClient apiClient, TokenManager tokenManager, NetworkMonitor networkMonitor)
        {
            _apiClient = apiClient;
            _tokenManager = tokenManager;
            _networkMonitor = networkMonitor;

            _deviceIdPath = Path.Combine(Application.persistentDataPath, DeviceIdFileName);
            _playerIdPath = Path.Combine(Application.persistentDataPath, PlayerIdFileName);
            _encryptionKey = DeriveKey();

            // Load persisted identifiers (if any)
            _deviceId = LoadEncrypted(_deviceIdPath);
            _playerId = LoadEncrypted(_playerIdPath);
        }

        /// <summary>
        /// Boot-time entry point. Attempts register (first launch) or refresh (subsequent).
        /// Silently falls back to offline mode on network failure.
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (!_networkMonitor.IsOnline)
            {
                Debug.Log("[AuthService] Offline — skipping authentication.");
                return false;
            }

            // Subsequent launch: we already have tokens → try refresh
            string existingRefresh = _tokenManager.GetRefreshToken();
            if (!string.IsNullOrEmpty(existingRefresh) && !string.IsNullOrEmpty(_playerId))
            {
                bool refreshed = await RefreshToken();
                if (refreshed)
                    return true;

                // Refresh failed but we have a deviceId → try re-register (idempotent)
                if (!string.IsNullOrEmpty(_deviceId))
                    return await Register();

                return false;
            }

            // First launch (or missing data) → register
            return await Register();
        }

        /// <summary>
        /// Anonymous device registration. Generates deviceId on first call.
        /// Idempotent: server returns existing player if deviceId is already registered.
        /// </summary>
        public async Task<bool> Register()
        {
            if (!_networkMonitor.IsOnline)
            {
                Debug.Log("[AuthService] Offline — skipping registration.");
                return false;
            }

            // Generate or reuse deviceId
            if (string.IsNullOrEmpty(_deviceId))
            {
                _deviceId = GenerateUuidV7();
                SaveEncrypted(_deviceIdPath, _deviceId);
            }

            var request = new RegisterRequest
            {
                DeviceId = _deviceId,
                Platform = GetPlatform(),
                ClientVersion = Application.version
            };

            var result = await _apiClient.Post<RegisterResponse>(
                ApiEndpoints.AuthRegister, request);

            if (!result.IsSuccess || result.Data == null)
            {
                Debug.LogWarning($"[AuthService] Registration failed: " +
                    $"{result.Error?.Code ?? "UNKNOWN"} — {result.Error?.Message}");
                return false;
            }

            _playerId = result.Data.PlayerId;
            SaveEncrypted(_playerIdPath, _playerId);

            _tokenManager.SetTokens(
                result.Data.AccessToken,
                result.Data.RefreshToken,
                result.Data.ExpiresIn);

            _isAuthenticated = true;
            Debug.Log($"[AuthService] Registered — playerId={_playerId}");
            return true;
        }

        /// <summary>
        /// Proactive token refresh (e.g. at boot). On 401 → offline mode (game not blocked).
        /// </summary>
        public async Task<bool> RefreshToken()
        {
            string refreshToken = _tokenManager.GetRefreshToken();
            if (string.IsNullOrEmpty(refreshToken))
                return false;

            if (!_networkMonitor.IsOnline)
            {
                Debug.Log("[AuthService] Offline — skipping token refresh.");
                return false;
            }

            var request = new RefreshRequest { RefreshToken = refreshToken };
            var result = await _apiClient.Post<RefreshResponse>(
                ApiEndpoints.AuthRefresh, request);

            if (!result.IsSuccess || result.Data == null)
            {
                Debug.LogWarning("[AuthService] Token refresh failed — entering offline mode.");
                _tokenManager.ClearTokens();
                _isAuthenticated = false;
                return false;
            }

            _tokenManager.SetTokens(
                result.Data.AccessToken,
                result.Data.RefreshToken,
                result.Data.ExpiresIn);

            _isAuthenticated = true;
            Debug.Log("[AuthService] Token refreshed successfully.");
            return true;
        }

        /// <summary>
        /// Link a third-party account (Google Play Games / Apple Game Center).
        /// Returns the result; caller handles UI feedback.
        /// </summary>
        public async Task<LinkResult> LinkAccount(string provider, string providerToken)
        {
            if (!_isAuthenticated)
                return new LinkResult { Success = false, ErrorCode = "NOT_AUTHENTICATED" };

            if (!_networkMonitor.IsOnline)
                return new LinkResult { Success = false, ErrorCode = "OFFLINE" };

            var request = new LinkRequest
            {
                Provider = provider,
                ProviderToken = providerToken
            };

            var result = await _apiClient.Post<LinkResponse>(
                ApiEndpoints.AuthLink, request);

            if (result.IsSuccess && result.Data != null)
            {
                return new LinkResult
                {
                    Success = true,
                    Provider = result.Data.Provider,
                    DisplayName = result.Data.DisplayName
                };
            }

            string errorCode = result.Error?.Code ?? "UNKNOWN";
            Debug.LogWarning($"[AuthService] LinkAccount failed: {errorCode}");

            return new LinkResult
            {
                Success = false,
                ErrorCode = errorCode,
                IsAlreadyLinked = errorCode == "ACCOUNT_ALREADY_LINKED"
            };
        }

        #region UUID v7

        /// <summary>
        /// Generate a UUID v7 (RFC 9562): 48-bit ms timestamp + random, version=7, variant=10.
        /// </summary>
        static string GenerateUuidV7()
        {
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            byte[] bytes = new byte[16];

            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            // Timestamp: big-endian 48-bit into bytes[0..5]
            bytes[0] = (byte)((ms >> 40) & 0xFF);
            bytes[1] = (byte)((ms >> 32) & 0xFF);
            bytes[2] = (byte)((ms >> 24) & 0xFF);
            bytes[3] = (byte)((ms >> 16) & 0xFF);
            bytes[4] = (byte)((ms >> 8) & 0xFF);
            bytes[5] = (byte)(ms & 0xFF);

            // Version 7: bytes[6] high nibble = 0111
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

            // Variant 10: bytes[8] top 2 bits = 10
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

            var guid = new Guid(bytes);
            return guid.ToString("D");
        }

        #endregion

        #region Encrypted persistence

        void SaveEncrypted(string filePath, string data)
        {
            try
            {
                byte[] plaintext = Encoding.UTF8.GetBytes(data);

                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Key = _encryptionKey;
                aes.GenerateIV();

                using var encryptor = aes.CreateEncryptor();
                byte[] ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                fs.Write(aes.IV, 0, AesIvSizeBytes);
                fs.Write(ciphertext, 0, ciphertext.Length);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuthService] Failed to save encrypted data to {Path.GetFileName(filePath)}: {e.Message}");
            }
        }

        string LoadEncrypted(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                byte[] fileBytes = File.ReadAllBytes(filePath);
                if (fileBytes.Length <= AesIvSizeBytes)
                    return null;

                byte[] iv = new byte[AesIvSizeBytes];
                Buffer.BlockCopy(fileBytes, 0, iv, 0, AesIvSizeBytes);

                int cipherLen = fileBytes.Length - AesIvSizeBytes;
                byte[] ciphertext = new byte[cipherLen];
                Buffer.BlockCopy(fileBytes, AesIvSizeBytes, ciphertext, 0, cipherLen);

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
                Debug.LogWarning($"[AuthService] Failed to load encrypted data from {Path.GetFileName(filePath)}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Same derivation as TokenManager — SHA-256(deviceUniqueIdentifier + hardware fingerprint).
        /// </summary>
        static byte[] DeriveKey()
        {
            string material = SystemInfo.deviceUniqueIdentifier
                + "|" + SystemInfo.deviceModel
                + "|" + SystemInfo.graphicsDeviceID
                + "|" + SystemInfo.processorType;

            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(material));
        }

        static string GetPlatform()
        {
#if UNITY_ANDROID
            return "android";
#elif UNITY_IOS
            return "ios";
#else
            return "editor";
#endif
        }

        #endregion

        #region DTO / Result types

        [Serializable]
        class RegisterRequest
        {
            [JsonProperty("deviceId")] public string DeviceId;
            [JsonProperty("platform")] public string Platform;
            [JsonProperty("clientVersion")] public string ClientVersion;
        }

        [Serializable]
        class RegisterResponse
        {
            [JsonProperty("playerId")] public string PlayerId;
            [JsonProperty("accessToken")] public string AccessToken;
            [JsonProperty("refreshToken")] public string RefreshToken;
            [JsonProperty("expiresIn")] public int ExpiresIn;
        }

        [Serializable]
        class RefreshRequest
        {
            [JsonProperty("refreshToken")] public string RefreshToken;
        }

        [Serializable]
        class RefreshResponse
        {
            [JsonProperty("accessToken")] public string AccessToken;
            [JsonProperty("refreshToken")] public string RefreshToken;
            [JsonProperty("expiresIn")] public int ExpiresIn;
        }

        [Serializable]
        class LinkRequest
        {
            [JsonProperty("provider")] public string Provider;
            [JsonProperty("providerToken")] public string ProviderToken;
        }

        [Serializable]
        class LinkResponse
        {
            [JsonProperty("linked")] public bool Linked;
            [JsonProperty("provider")] public string Provider;
            [JsonProperty("displayName")] public string DisplayName;
        }

        /// <summary>
        /// Public result returned by <see cref="LinkAccount"/>.
        /// </summary>
        public class LinkResult
        {
            public bool Success;
            public string Provider;
            public string DisplayName;
            public string ErrorCode;
            public bool IsAlreadyLinked;
        }

        #endregion
    }
}
