using System;
using System.IO;

namespace YouTubeShortsWebApp
{
    public class ConfigManager
    {
        private static readonly string ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YouTubeShortsWebApp",
            "config.json"
        );

        public class Config
        {
            public string ReplicateApiKey { get; set; } = "";
            public string YouTubeClientId { get; set; } = "";
            public string YouTubeClientSecret { get; set; } = "";
            public string LastOutputDirectory { get; set; } = "";
            public string DefaultVideoTitle { get; set; } = "AI Generated Video";
            public string DefaultVideoDescription { get; set; } = "Generated using YouTube Shorts Generator";
            public string DefaultVideoTags { get; set; } = "AI,Video,Generated,Shorts";
            public string DefaultPrivacySetting { get; set; } = "🔒 비공개";
            public string BasePrompt { get; set; } = "";
        }

        private static Config _config = null;

        public static Config GetConfig()
        {
            if (_config == null)
            {
                LoadConfig();
            }
            return _config;
        }

        public static void LoadConfig()
        {
            try
            {
                _config = new Config();

                // 환경변수에서 먼저 읽기 (클라우드 배포용)
                _config.ReplicateApiKey = Environment.GetEnvironmentVariable("REPLICATE_API_KEY") ?? "";
                _config.YouTubeClientId = Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_ID") ?? "";
                _config.YouTubeClientSecret = Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_SECRET") ?? "";
                _config.BasePrompt = Environment.GetEnvironmentVariable("BASE_PROMPT") ?? "";

                // 환경변수가 없으면 로컬 파일에서 읽기
                if (string.IsNullOrEmpty(_config.ReplicateApiKey) && File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var fileConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(json);
                    if (fileConfig != null)
                    {
                        if (string.IsNullOrEmpty(_config.ReplicateApiKey))
                            _config.ReplicateApiKey = fileConfig.ReplicateApiKey ?? "";
                        if (string.IsNullOrEmpty(_config.YouTubeClientId))
                            _config.YouTubeClientId = fileConfig.YouTubeClientId ?? "";
                        if (string.IsNullOrEmpty(_config.YouTubeClientSecret))
                            _config.YouTubeClientSecret = fileConfig.YouTubeClientSecret ?? "";
                        if (string.IsNullOrEmpty(_config.BasePrompt))
                            _config.BasePrompt = fileConfig.BasePrompt ?? "";

                        _config.DefaultVideoTitle = fileConfig.DefaultVideoTitle ?? "AI Generated Video";
                        _config.DefaultVideoDescription = fileConfig.DefaultVideoDescription ?? "Generated using YouTube Shorts Generator";
                        _config.DefaultVideoTags = fileConfig.DefaultVideoTags ?? "AI,Video,Generated,Shorts";
                        _config.DefaultPrivacySetting = fileConfig.DefaultPrivacySetting ?? "🔒 비공개";
                        _config.LastOutputDirectory = fileConfig.LastOutputDirectory ?? "";
                    }
                }

                // null 값 방지
                if (_config.BasePrompt == null)
                    _config.BasePrompt = "";
                if (_config.ReplicateApiKey == null)
                    _config.ReplicateApiKey = "";
                if (_config.YouTubeClientId == null)
                    _config.YouTubeClientId = "";
                if (_config.YouTubeClientSecret == null)
                    _config.YouTubeClientSecret = "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"설정 파일 로드 중 오류 발생: {ex.Message}");
                _config = new Config();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                // 클라우드 환경에서는 파일 저장을 시도하지 않음
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER")))
                {
                    Console.WriteLine("클라우드 환경에서는 설정이 환경변수로 관리됩니다.");
                    return;
                }

                // 디렉토리가 없으면 생성
                string directory = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(_config, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(ConfigFilePath, json);

                System.Diagnostics.Debug.WriteLine($"설정 저장됨: {ConfigFilePath}");
                System.Diagnostics.Debug.WriteLine($"기본 프롬프트: {_config.BasePrompt}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"설정 파일 저장 중 오류 발생: {ex.Message}");
            }
        }

        // Replicate API 키 관련
        public static void SetReplicateApiKey(string apiKey)
        {
            GetConfig().ReplicateApiKey = apiKey ?? "";
            SaveConfig();
        }

        public static bool IsReplicateApiKeySet()
        {
            return !string.IsNullOrEmpty(GetConfig().ReplicateApiKey);
        }

        // YouTube API 관련
        public static void SetYouTubeCredentials(string clientId, string clientSecret)
        {
            var config = GetConfig();
            config.YouTubeClientId = clientId ?? "";
            config.YouTubeClientSecret = clientSecret ?? "";
            SaveConfig();
        }

        public static bool IsYouTubeCredentialsSet()
        {
            var config = GetConfig();
            return !string.IsNullOrEmpty(config.YouTubeClientId) && !string.IsNullOrEmpty(config.YouTubeClientSecret);
        }

        // 기본 업로드 설정 관련
        public static void SetDefaultUploadSettings(string title, string description, string tags, string privacy)
        {
            var config = GetConfig();
            config.DefaultVideoTitle = title ?? "";
            config.DefaultVideoDescription = description ?? "";
            config.DefaultVideoTags = tags ?? "";
            config.DefaultPrivacySetting = privacy ?? "";
            SaveConfig();
        }

        // 기본 프롬프트 관련 메서드
        public static void SetBasePrompt(string basePrompt)
        {
            GetConfig().BasePrompt = basePrompt ?? "";
            SaveConfig();
            System.Diagnostics.Debug.WriteLine($"기본 프롬프트 저장: '{basePrompt}'");
        }

        public static string GetBasePrompt()
        {
            string basePrompt = GetConfig().BasePrompt ?? "";
            System.Diagnostics.Debug.WriteLine($"기본 프롬프트 로드: '{basePrompt}'");
            return basePrompt;
        }

        // 기본 프롬프트와 사용자 프롬프트를 합성하는 메서드 (개선)
        public static string CombinePrompts(string userPrompt)
        {
            string basePrompt = GetBasePrompt().Trim();
            string userPromptTrimmed = (userPrompt ?? "").Trim();

            System.Diagnostics.Debug.WriteLine($"=== 프롬프트 합성 시작 ===");
            System.Diagnostics.Debug.WriteLine($"기본 프롬프트: '{basePrompt}'");
            System.Diagnostics.Debug.WriteLine($"사용자 프롬프트: '{userPromptTrimmed}'");

            // 둘 다 비어있는 경우
            if (string.IsNullOrEmpty(basePrompt) && string.IsNullOrEmpty(userPromptTrimmed))
            {
                System.Diagnostics.Debug.WriteLine("경고: 기본 프롬프트와 사용자 프롬프트가 모두 비어있음");
                return "";
            }

            // 기본 프롬프트만 있는 경우
            if (string.IsNullOrEmpty(userPromptTrimmed))
            {
                System.Diagnostics.Debug.WriteLine($"기본 프롬프트만 사용: '{basePrompt}'");
                return basePrompt;
            }

            // 사용자 프롬프트만 있는 경우
            if (string.IsNullOrEmpty(basePrompt))
            {
                System.Diagnostics.Debug.WriteLine($"사용자 프롬프트만 사용: '{userPromptTrimmed}'");
                return userPromptTrimmed;
            }

            // 둘 다 있는 경우 합성
            string combinedPrompt = $"{basePrompt}, {userPromptTrimmed}";
            System.Diagnostics.Debug.WriteLine($"합성된 프롬프트: '{combinedPrompt}'");
            System.Diagnostics.Debug.WriteLine($"=== 프롬프트 합성 완료 ===");

            return combinedPrompt;
        }

        // 설정이 유효한지 확인하는 메서드 추가
        public static bool ValidateConfig()
        {
            try
            {
                var config = GetConfig();
                System.Diagnostics.Debug.WriteLine($"설정 검증 - 기본 프롬프트: '{config.BasePrompt}'");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 검증 실패: {ex.Message}");
                return false;
            }
        }
    }
}