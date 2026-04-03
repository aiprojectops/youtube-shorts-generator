using Microsoft.JSInterop;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace YouTubeShortsWebApp.Services
{
    /// <summary>
    /// 사용자별 설정 관리 서비스 (UserId 기반 격리)
    /// </summary>
    public class UserSettingsService
    {
        private readonly IJSRuntime _jsRuntime;
        private string _userId;
        // UserId -> UserSettings 매핑 (메모리 저장)
        private static readonly ConcurrentDictionary<string, UserSettings> _userSettingsStore 
            = new ConcurrentDictionary<string, UserSettings>();


        public UserSettingsService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public string GetUserId()
        {
            return _userId ?? "";
        }

        /// <summary>
        /// 사용자 설정 클래스
        /// </summary>
        public class UserSettings
        {
            public string ReplicateApiKey { get; set; } = "";
            public string BasePrompt { get; set; } = "";
        }

        /// <summary>
        /// UserId 초기화 (쿠키에서 로드 또는 생성)
        /// </summary>
        public async Task InitializeAsync()
        {
            if (string.IsNullOrEmpty(_userId))
            {
                _userId = await GetOrCreateUserIdAsync();
                Console.WriteLine($"=== UserSettingsService 초기화: UserId={_userId}");
            }
        }

        /// <summary>
        /// 쿠키에서 UserId 가져오기 또는 새로 생성
        /// </summary>
        private async Task<string> GetOrCreateUserIdAsync()
        {
            try
            {
                string userId = await _jsRuntime.InvokeAsync<string>("getCookie", "userId");
                
                if (string.IsNullOrEmpty(userId))
                {
                    userId = Guid.NewGuid().ToString();
                    await _jsRuntime.InvokeVoidAsync("setCookie", "userId", userId, 30);
                    Console.WriteLine($"=== 🆕 새 UserId 생성: {userId}");
                }
                else
                {
                    Console.WriteLine($"=== ♻️ 쿠키에서 UserId 로드: {userId}");
                }
                
                return userId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== ⚠️ 쿠키 처리 실패: {ex.Message}, 임시 ID 사용");
                return Guid.NewGuid().ToString();
            }
        }

        /// <summary>
        /// 현재 사용자의 설정 가져오기
        /// </summary>
        public UserSettings GetSettings()
        {
            if (string.IsNullOrEmpty(_userId))
            {
                Console.WriteLine("⚠️ UserId가 초기화되지 않음");
                return new UserSettings();
            }

            return _userSettingsStore.GetOrAdd(_userId, _ => new UserSettings());
        }

        /// <summary>
        /// Replicate API 키 저장
        /// </summary>
        public void SetReplicateApiKey(string apiKey)
        {
            var settings = GetSettings();
            settings.ReplicateApiKey = apiKey ?? "";
            
            Console.WriteLine($"=== Replicate API 키 저장: UserId={_userId}");
        }

        /// <summary>
        /// 기본 프롬프트 저장
        /// </summary>
        public void SetBasePrompt(string basePrompt)
        {
            var settings = GetSettings();
            settings.BasePrompt = basePrompt ?? "";
            
            Console.WriteLine($"=== 기본 프롬프트 저장: UserId={_userId}");
        }

        /// <summary>
        /// Replicate API 키 가져오기
        /// </summary>
        public string GetReplicateApiKey()
        {
            return GetSettings().ReplicateApiKey;
        }

        /// <summary>
        /// 기본 프롬프트 가져오기
        /// </summary>
        public string GetBasePrompt()
        {
            return GetSettings().BasePrompt;
        }

        /// <summary>
        /// 기본 프롬프트와 사용자 프롬프트를 합성
        /// </summary>
        public string CombinePrompts(string userPrompt)
        {
            string basePrompt = GetBasePrompt().Trim();
            string userPromptTrimmed = (userPrompt ?? "").Trim();

            if (string.IsNullOrEmpty(basePrompt) && string.IsNullOrEmpty(userPromptTrimmed))
            {
                return "";
            }

            if (string.IsNullOrEmpty(userPromptTrimmed))
            {
                return basePrompt;
            }

            if (string.IsNullOrEmpty(basePrompt))
            {
                return userPromptTrimmed;
            }

            return $"{basePrompt}, {userPromptTrimmed}";
        }

        /// <summary>
        /// 디버깅: 전체 사용자 수 출력
        /// </summary>
        public static void PrintStatus()
        {
            Console.WriteLine($"=== 📊 UserSettings 상태: 총 {_userSettingsStore.Count}명의 사용자");
        }
    }
}
