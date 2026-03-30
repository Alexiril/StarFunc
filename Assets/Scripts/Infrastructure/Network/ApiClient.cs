using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using UnityEngine.Networking;

namespace StarFunc.Infrastructure
{
    #region Response models

    [Serializable]
    public class ApiResponse<T>
    {
        [JsonProperty("status")] public string Status;
        [JsonProperty("data")] public T Data;
        [JsonProperty("serverTime")] public long ServerTime;
    }

    [Serializable]
    public class ApiErrorResponse
    {
        [JsonProperty("status")] public string Status;
        [JsonProperty("error")] public ApiError Error;
        [JsonProperty("serverTime")] public long ServerTime;
    }

    [Serializable]
    public class ApiError
    {
        [JsonProperty("code")] public string Code;
        [JsonProperty("message")] public string Message;
        [JsonProperty("details")] public object Details;
    }

    #endregion

    /// <summary>
    /// Result wrapper returned by ApiClient methods.
    /// </summary>
    public class ApiResult<T>
    {
        public bool IsSuccess;
        public T Data;
        public long ServerTime;
        public int HttpStatus;
        public ApiError Error;
        public bool WentOffline;
    }

    /// <summary>
    /// HTTP client wrapping UnityWebRequest (API.md §10).
    /// Handles headers, retry strategy (§4.3), timeouts, gzip, and token refresh.
    /// </summary>
    public class ApiClient
    {
        const int HardTimeoutSeconds = 10;
        const int MaxRetries = 3;

        static readonly float[] BackoffDelays = { 1f, 2f, 4f };

        readonly TokenManager _tokenManager;
        readonly NetworkMonitor _networkMonitor;

        static readonly JsonSerializerSettings JsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        public ApiClient(TokenManager tokenManager, NetworkMonitor networkMonitor)
        {
            _tokenManager = tokenManager;
            _networkMonitor = networkMonitor;
        }

        #region Public API

        public async Task<ApiResult<T>> Get<T>(string endpoint)
        {
            return await SendWithRetry<T>("GET", endpoint, null);
        }

        public async Task<ApiResult<T>> Post<T>(string endpoint, object body)
        {
            return await SendWithRetry<T>("POST", endpoint, body);
        }

        public async Task<ApiResult<T>> Put<T>(string endpoint, object body)
        {
            return await SendWithRetry<T>("PUT", endpoint, body);
        }

        #endregion

        #region Core send logic

        async Task<ApiResult<T>> SendWithRetry<T>(string method, string endpoint, object body)
        {
            bool isMutating = method is "POST" or "PUT";
            string idempotencyKey = isMutating ? Guid.NewGuid().ToString() : null;
            bool alreadyRefreshed = false;

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                var result = await SendRequest<T>(method, endpoint, body, idempotencyKey);

                // Success — return immediately
                if (result.IsSuccess)
                    return result;

                int status = result.HttpStatus;

                // 401 → refresh token once → retry
                if (status == 401 && !alreadyRefreshed)
                {
                    alreadyRefreshed = true;
                    bool refreshed = await TryRefreshToken();
                    if (refreshed)
                    {
                        // Retry immediately (don't count as backoff attempt)
                        continue;
                    }

                    // Refresh failed → offline
                    GoOffline();
                    result.WentOffline = true;
                    return result;
                }

                // 429 → exponential backoff
                if (status == 429 && attempt < MaxRetries)
                {
                    await Delay(BackoffDelays[Math.Min(attempt, BackoffDelays.Length - 1)]);
                    continue;
                }

                // 500/503 → exponential backoff
                if (status is 500 or 503 && attempt < MaxRetries)
                {
                    await Delay(BackoffDelays[Math.Min(attempt, BackoffDelays.Length - 1)]);
                    continue;
                }

                // Timeout → 1 retry
                if (status == 0 && attempt < 1)
                {
                    continue;
                }

                // Timeout after retry or server errors exhausted → offline
                if (status == 0 || status is 500 or 503)
                {
                    GoOffline();
                    result.WentOffline = true;
                }

                return result;
            }

            // Shouldn't reach here, but safeguard
            var offline = new ApiResult<T> { WentOffline = true };
            GoOffline();
            return offline;
        }

        async Task<ApiResult<T>> SendRequest<T>(string method, string endpoint, object body,
            string idempotencyKey)
        {
            string url = ApiEndpoints.BaseUrl + endpoint;
            string jsonBody = body != null ? JsonConvert.SerializeObject(body, JsonSettings) : null;

            using var request = CreateRequest(method, url, jsonBody);

            // Headers
            SetCommonHeaders(request, idempotencyKey);

            // Timeout
            request.timeout = HardTimeoutSeconds;

            // Send
            var operation = request.SendWebRequest();

            // Await completion
            while (!operation.isDone)
                await Task.Yield();

            return ParseResponse<T>(request);
        }

        static UnityWebRequest CreateRequest(string method, string url, string jsonBody)
        {
            switch (method)
            {
                case "GET":
                    return UnityWebRequest.Get(url);

                case "POST":
                case "PUT":
                    {
                        var request = new UnityWebRequest(url, method);
                        if (jsonBody != null)
                        {
                            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                        }

                        request.downloadHandler = new DownloadHandlerBuffer();
                        return request;
                    }

                default:
                    throw new ArgumentException($"Unsupported HTTP method: {method}");
            }
        }

        void SetCommonHeaders(UnityWebRequest request, string idempotencyKey)
        {
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept-Encoding", "gzip");
            request.SetRequestHeader("X-Client-Version", Application.version);
            request.SetRequestHeader("X-Platform", GetPlatform());

            string token = _tokenManager.GetAccessToken();
            if (!string.IsNullOrEmpty(token))
                request.SetRequestHeader("Authorization", $"Bearer {token}");

            if (!string.IsNullOrEmpty(idempotencyKey))
                request.SetRequestHeader("Idempotency-Key", idempotencyKey);
        }

        #endregion

        #region Response parsing

        ApiResult<T> ParseResponse<T>(UnityWebRequest request)
        {
            var result = new ApiResult<T>
            {
                HttpStatus = (int)request.responseCode
            };

            // Network error / timeout
            if (request.result is UnityWebRequest.Result.ConnectionError
                or UnityWebRequest.Result.DataProcessingError)
            {
                result.HttpStatus = 0;
                result.Error = new ApiError
                {
                    Code = "NETWORK_ERROR",
                    Message = request.error
                };
                return result;
            }

            string responseText = request.downloadHandler?.text;
            if (string.IsNullOrEmpty(responseText))
            {
                // Health endpoint or empty success
                if (request.responseCode is >= 200 and < 300)
                {
                    result.IsSuccess = true;
                    return result;
                }

                result.Error = new ApiError
                {
                    Code = "EMPTY_RESPONSE",
                    Message = $"HTTP {request.responseCode} with empty body"
                };
                return result;
            }

            // Success range
            if (request.responseCode is >= 200 and < 300)
            {
                try
                {
                    var response = JsonConvert.DeserializeObject<ApiResponse<T>>(responseText);
                    result.IsSuccess = true;
                    result.Data = response.Data;
                    result.ServerTime = response.ServerTime;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ApiClient] Failed to deserialize success response: {e.Message}");
                    result.Error = new ApiError
                    {
                        Code = "DESERIALIZATION_ERROR",
                        Message = e.Message
                    };
                }

                return result;
            }

            // Error response
            try
            {
                var errorResponse = JsonConvert.DeserializeObject<ApiErrorResponse>(responseText);
                result.Error = errorResponse?.Error ?? new ApiError
                {
                    Code = "UNKNOWN_ERROR",
                    Message = $"HTTP {request.responseCode}"
                };
                result.ServerTime = errorResponse?.ServerTime ?? 0;
            }
            catch
            {
                result.Error = new ApiError
                {
                    Code = "PARSE_ERROR",
                    Message = responseText
                };
            }

            return result;
        }

        #endregion

        #region Token refresh

        async Task<bool> TryRefreshToken()
        {
            string refreshToken = _tokenManager.GetRefreshToken();
            if (string.IsNullOrEmpty(refreshToken))
                return false;

            var body = new { refreshToken };
            var result = await SendRequest<RefreshResponse>(
                "POST", ApiEndpoints.AuthRefresh, body, null);

            if (!result.IsSuccess || result.Data == null)
            {
                _tokenManager.ClearTokens();
                return false;
            }

            _tokenManager.SetTokens(
                result.Data.AccessToken,
                result.Data.RefreshToken,
                result.Data.ExpiresIn);

            return true;
        }

        [Serializable]
        class RefreshResponse
        {
            [JsonProperty("accessToken")] public string AccessToken;
            [JsonProperty("refreshToken")] public string RefreshToken;
            [JsonProperty("expiresIn")] public int ExpiresIn;
        }

        #endregion

        #region Helpers

        void GoOffline()
        {
            _networkMonitor.SetOffline();
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

        static async Task Delay(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                await Task.Yield();
        }

        #endregion
    }
}
