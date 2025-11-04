using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace YouTubeShortsWebApp.Services
{
/// <summary>
/// YouTube 업로드 관련 공통 로직을 처리하는 서비스
/// </summary>
public class YouTubeUploadService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly SharedMemoryDataStore _dataStore;  // 🆕 이 줄 추가

    // 🔥 사용자별 고유 ID (쿠키로 관리)
    private string _userId;
    
    private YouTubeUploader _youtubeUploader;
    private YouTubeUploader.YouTubeAccountInfo _currentAccount;  // 🔥 수정
    
    public bool IsAuthenticated => _youtubeUploader != null && _currentAccount != null;
    public YouTubeUploader.YouTubeAccountInfo CurrentAccount => _currentAccount;  // 🔥 수정

    public YouTubeUploadService(IJSRuntime jsRuntime, SharedMemoryDataStore dataStore)  // 🆕 매개변수 추가
    {
        _jsRuntime = jsRuntime;
        _dataStore = dataStore;  // 🆕 이 줄 추가
        _userId = null; // 나중에 InitializeAsync에서 설정
        Console.WriteLine($"=== YouTubeUploadService 생성 (UserId는 아직 미설정)");
    }
    
    /// <summary>
    /// UserId 초기화 (쿠키에서 로드 또는 생성)
    /// </summary>
    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(_userId))
        {
            _userId = await GetOrCreateUserIdAsync();
            Console.WriteLine($"=== YouTubeUploadService 초기화 완료: UserId={_userId}");
        }
    }

    public string GetUserId()
    {
        return _userId;
    }

    /// <summary>
    /// 쿠키에서 UserId 가져오기 또는 새로 생성
    /// </summary>
    private async Task<string> GetOrCreateUserIdAsync()
    {
        try
        {
            // 쿠키에서 UserId 읽기
            string userId = await _jsRuntime.InvokeAsync<string>("getCookie", "userId");
            
            if (string.IsNullOrEmpty(userId))
            {
                // 없으면 새로 생성
                userId = Guid.NewGuid().ToString();
                // 쿠키에 저장 (30일 유효)
                await _jsRuntime.InvokeVoidAsync("setCookie", "userId", userId, 30);
                Console.WriteLine($"=== 🆕 새 UserId 생성 및 쿠키 저장: {userId}");
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
    /// YouTube 인증 URL 가져오기
    /// </summary>
    public async Task<string> GetAuthorizationUrlAsync(IJSRuntime jsRuntime, string returnPage = "youtube-upload")
    {
        var config = ConfigManager.GetConfig();
        if (string.IsNullOrEmpty(config.YouTubeClientId) || string.IsNullOrEmpty(config.YouTubeClientSecret))
        {
            throw new Exception("먼저 설정에서 YouTube API 정보를 입력해주세요.");
        }

        // 🔥 userId와 dataStore 전달 
        _youtubeUploader = new YouTubeUploader(_userId, _dataStore);  // 🆕 _dataStore 추가
        string currentUrl = await jsRuntime.InvokeAsync<string>("eval", "window.location.origin");        
        // 🔥 returnPage에 userId 포함
        string stateWithUserId = $"{returnPage}|{_userId}";
        return await _youtubeUploader.GetAuthorizationUrlAsync(currentUrl, stateWithUserId);
    }

    /// <summary>
    /// 기존 인증 확인
    /// </summary>
    public async Task<bool> CheckExistingAuthAsync()
    {
        try
        {
            var config = ConfigManager.GetConfig();
            if (string.IsNullOrEmpty(config.YouTubeClientId) || string.IsNullOrEmpty(config.YouTubeClientSecret))
            {
                return false;
            }

            // 🔥 userId와 dataStore 전달 
            _youtubeUploader = new YouTubeUploader(_userId, _dataStore);  // 🆕 _dataStore 추가
            bool authSuccess = await _youtubeUploader.AuthenticateAsync();

            if (authSuccess)
            {
                _currentAccount = await _youtubeUploader.GetCurrentAccountInfoAsync();
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"기존 YouTube 인증 확인 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 인증 코드로 토큰 교환
    /// </summary>
    public async Task<bool> ExchangeCodeForTokenAsync(string code, string baseUrl)
    {
        try
        {
            // 🔥 userId와 dataStore 전달
            _youtubeUploader = new YouTubeUploader(_userId, _dataStore);  // 🆕 _dataStore 추가
            bool success = await _youtubeUploader.ExchangeCodeForTokenAsync(code, baseUrl);

            if (success)
            {
                _currentAccount = await _youtubeUploader.GetCurrentAccountInfoAsync();
            }

            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"토큰 교환 실패: {ex.Message}");
            return false;
        }
    }

     
    /// <summary>
    /// 계정 전환
    /// </summary>
    public async Task SwitchAccountAsync()
    {
        if (_youtubeUploader != null)
        {
            await _youtubeUploader.RevokeAuthenticationAsync();
        }

        _youtubeUploader = null;
        _currentAccount = null;
    }

    /// <summary>
    /// 단일 파일 업로드
    /// </summary>
    public async Task<string> UploadSingleVideoAsync(
        string filePath,
        string title,
        UploadOptions options,
        IProgress<YouTubeUploader.UploadProgressInfo> progress = null)
    {
        if (_youtubeUploader == null || _currentAccount == null)
        {
            throw new Exception("YouTube 인증이 필요합니다.");
        }

        // 🔥 YouTube 업로드 시작 로그
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"📤 [YouTube 업로드 시작]");
        Console.WriteLine($"📝 제목: {title}");
        Console.WriteLine($"📄 설명: {options.Description}");
        Console.WriteLine($"🏷️ 태그: {options.Tags}");
        Console.WriteLine($"🔒 공개 설정: {options.PrivacySetting}");
        Console.WriteLine($"📁 파일: {Path.GetFileName(filePath)}");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        var uploadInfo = new YouTubeUploader.VideoUploadInfo
        {
            FilePath = filePath,
            Title = title,
            Description = options.Description,
            Tags = options.Tags,
            PrivacyStatus = options.PrivacySetting
        };

        string videoUrl = await _youtubeUploader.UploadVideoAsync(uploadInfo, progress);

        // 🔥 YouTube 업로드 완료 로그
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"✅ [YouTube 업로드 완료]");
        Console.WriteLine($"📝 제목: {title}");
        Console.WriteLine($"🔗 URL: {videoUrl}");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        return videoUrl;
    }


    // RandomUploadInfo 클래스를 별도 리스트로 분리
    public class UploadOptions
    {
        public string TitleTemplate { get; set; } = "";
        public string Description { get; set; } = "";
        public string Tags { get; set; } = "";
        public string PrivacySetting { get; set; } = "공개";
        public bool UseRandomInfo { get; set; } = false;
        
        // 🔥 각각 독립적인 리스트로 변경
        public List<string>? RandomTitles { get; set; } = null;
        public List<string>? RandomDescriptions { get; set; } = null;
        public List<string>? RandomTags { get; set; } = null;
    }
    
   
  
   /// <summary>
  /// 여러 파일 즉시 업로드
  /// </summary>
  public async Task<List<string>> UploadMultipleVideosAsync(
      List<string> filePaths,
      UploadOptions options,
      Action<int, int, string>? progressCallback = null)
  {
      var uploadedUrls = new List<string>();
      var random = new Random();
      
      Console.WriteLine($"=== 업로드 시작: {filePaths.Count}개 파일");
      
      if (options.UseRandomInfo)
      {
          Console.WriteLine($"=== 랜덤 업로드 정보 활성화");
          Console.WriteLine($"    제목 풀: {options.RandomTitles?.Count ?? 0}개");
          Console.WriteLine($"    설명 풀: {options.RandomDescriptions?.Count ?? 0}개");
          Console.WriteLine($"    태그 풀: {options.RandomTags?.Count ?? 0}개");
      }
      
      for (int i = 0; i < filePaths.Count; i++)
      {
          try
          {
              string filePath = filePaths[i];
              string title, description, tags;
              
              if (options.UseRandomInfo)
              {
                  // 🔥 각각 완전히 랜덤하게 선택 (매번 새로운 랜덤)
                  title = options.RandomTitles != null && options.RandomTitles.Count > 0
                      ? options.RandomTitles[random.Next(options.RandomTitles.Count)]
                      : (filePaths.Count > 1 ? $"{options.TitleTemplate} #{i + 1}" : options.TitleTemplate);
                  
                  description = options.RandomDescriptions != null && options.RandomDescriptions.Count > 0
                      ? options.RandomDescriptions[random.Next(options.RandomDescriptions.Count)]
                      : options.Description;
                  
                  tags = options.RandomTags != null && options.RandomTags.Count > 0
                      ? options.RandomTags[random.Next(options.RandomTags.Count)]
                      : options.Tags;
                  
                  Console.WriteLine($"=== 영상 {i + 1}: 완전 랜덤 조합");
                  Console.WriteLine($"    제목: {title.Substring(0, Math.Min(30, title.Length))}...");
                  Console.WriteLine($"    설명: {description.Substring(0, Math.Min(30, description.Length))}...");
                  Console.WriteLine($"    태그: {tags.Substring(0, Math.Min(30, tags.Length))}...");
              }
              else
              {
                  // 기본 템플릿 사용
                  title = filePaths.Count > 1 
                      ? $"{options.TitleTemplate} #{i + 1}" 
                      : options.TitleTemplate;
                  description = options.Description;
                  tags = options.Tags;
                  
                  Console.WriteLine($"=== 영상 {i + 1}: 템플릿 사용 - {title}");
              }
  
              progressCallback?.Invoke(i + 1, filePaths.Count, title);

              // 🔥 YouTube 업로드 시작 로그
              Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
              Console.WriteLine($"📤 [YouTube 업로드 시작] 영상 {i + 1}/{filePaths.Count}");
              Console.WriteLine($"📝 제목: {title}");
              Console.WriteLine($"📄 설명: {description}");
              Console.WriteLine($"🏷️ 태그: {tags}");
              Console.WriteLine($"🔒 공개 설정: {options.PrivacySetting}");
              Console.WriteLine($"📁 파일: {Path.GetFileName(filePath)}");
              Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
  
              var uploadInfo = new YouTubeUploader.VideoUploadInfo
              {
                  FilePath = filePath,
                  Title = title,
                  Description = description,
                  Tags = tags,
                  PrivacyStatus = options.PrivacySetting
              };
  
              string videoUrl = await _youtubeUploader.UploadVideoAsync(uploadInfo);
              uploadedUrls.Add(videoUrl);

              // 🔥 YouTube 업로드 완료 로그
              Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
              Console.WriteLine($"✅ [YouTube 업로드 완료] 영상 {i + 1}/{filePaths.Count}");
              Console.WriteLine($"📝 제목: {title}");
              Console.WriteLine($"🔗 URL: {videoUrl}");
              Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
          }
          catch (Exception ex)
          {
              Console.WriteLine($"❌ 업로드 실패 [{i + 1}]: {ex.Message}");
          }
      }
  
      return uploadedUrls;
  }
  
    /// <summary>
    /// 스케줄 업로드 등록 (생성 정보 포함)
    /// </summary>
    public void RegisterScheduledUploadsWithGeneration(
        List<VideoGenerationInfo> videoInfoList,
        UploadOptions uploadOptions,
        Dictionary<int, DateTime> scheduledTimes,
        bool randomizeOrder,
        ScheduledUploadService scheduledUploadService)
    {
        // 🔥 현재 UserId와 RefreshToken 가져오기
        string currentUserId = _userId;
        string refreshToken = GetCurrentRefreshToken();
        Console.WriteLine($"=== [생성+스케줄] UserId: {currentUserId}, RefreshToken 있음: {!string.IsNullOrEmpty(refreshToken)}");
        
        var videosToSchedule = randomizeOrder
            ? videoInfoList.OrderBy(x => Guid.NewGuid()).ToList()
            : videoInfoList.ToList();
        
        Console.WriteLine($"=== 생성 정보 스케줄 등록: {videosToSchedule.Count}개");
        
        if (uploadOptions.UseRandomInfo)
        {
            Console.WriteLine($"=== 랜덤 업로드 정보 활성화");
            Console.WriteLine($"    제목 풀: {uploadOptions.RandomTitles?.Count ?? 0}개");
            Console.WriteLine($"    설명 풀: {uploadOptions.RandomDescriptions?.Count ?? 0}개");
            Console.WriteLine($"    태그 풀: {uploadOptions.RandomTags?.Count ?? 0}개");
        }
    
        for (int i = 0; i < videosToSchedule.Count; i++)
        {
            var videoInfo = videosToSchedule[i];
            
            DateTime scheduledTime = scheduledTimes.ContainsKey(i) 
                ? scheduledTimes[i] 
                : DateTime.Now.AddMinutes(5 + (i * 10));
    
            string title, description, tags;
            
            if (uploadOptions.UseRandomInfo)
            {
                var titleRandom = new Random(Guid.NewGuid().GetHashCode());
                var descRandom = new Random(Guid.NewGuid().GetHashCode());
                var tagsRandom = new Random(Guid.NewGuid().GetHashCode());
                
                title = uploadOptions.RandomTitles != null && uploadOptions.RandomTitles.Count > 0
                    ? uploadOptions.RandomTitles[titleRandom.Next(uploadOptions.RandomTitles.Count)]
                    : (videosToSchedule.Count > 1 
                        ? uploadOptions.TitleTemplate.Replace("#NUMBER", $"#{i + 1}")
                        : uploadOptions.TitleTemplate.Replace(" #NUMBER", ""));
                
                description = uploadOptions.RandomDescriptions != null && uploadOptions.RandomDescriptions.Count > 0
                    ? uploadOptions.RandomDescriptions[descRandom.Next(uploadOptions.RandomDescriptions.Count)]
                    : uploadOptions.Description;
                
                tags = uploadOptions.RandomTags != null && uploadOptions.RandomTags.Count > 0
                    ? uploadOptions.RandomTags[tagsRandom.Next(uploadOptions.RandomTags.Count)]
                    : uploadOptions.Tags;
            }
            else
            {
                title = videosToSchedule.Count > 1
                    ? uploadOptions.TitleTemplate.Replace("#NUMBER", $"#{i + 1}")
                    : uploadOptions.TitleTemplate.Replace(" #NUMBER", "");
                description = uploadOptions.Description;
                tags = uploadOptions.Tags;
            }
    
            var uploadItem = new ScheduledUploadItem
            {
                FileName = $"video_{i + 1:D3}.mp4",
                ScheduledTime = scheduledTime,
    
                // 🔥 UserId와 RefreshToken 추가
                UserId = currentUserId,
                RefreshToken = refreshToken,
                
                Title = title,
                Description = description,
                Tags = tags,
                PrivacySetting = uploadOptions.PrivacySetting,
                
                // 생성 정보
                NeedsGeneration = true,
                Prompt = videoInfo.Prompt,
                Duration = videoInfo.Duration,
                AspectRatio = videoInfo.AspectRatio,
                EnablePostProcessing = videoInfo.EnablePostProcessing,
                CaptionText = videoInfo.CaptionText,
                CaptionPosition = videoInfo.CaptionPosition,
                CaptionSize = videoInfo.CaptionSize,
                CaptionColor = videoInfo.CaptionColor,
                AddBackgroundMusic = videoInfo.AddBackgroundMusic,
                MusicFilePath = videoInfo.MusicFilePath,
                MusicVolume = videoInfo.MusicVolume
            };
    
            scheduledUploadService.AddScheduledUpload(_userId, uploadItem);
        }
        
        Console.WriteLine($"=== 스케줄 등록 완료: {videosToSchedule.Count}개");
    }
     
    public class VideoGenerationInfo
    {
        public string Prompt { get; set; } = "";
        public int Duration { get; set; } = 5;
        public string AspectRatio { get; set; } = "9:16";
        public bool EnablePostProcessing { get; set; } = false;
        public string? CaptionText { get; set; }
        public string? CaptionPosition { get; set; }
        public string? CaptionSize { get; set; }
        public string? CaptionColor { get; set; }
        public bool AddBackgroundMusic { get; set; } = false;
        public string? MusicFilePath { get; set; }
        public float MusicVolume { get; set; } = 0.3f;
    }
  
    /// <summary>
    /// 스케줄 업로드 등록
    /// </summary>
    public void RegisterScheduledUploads(
        List<string> filePaths,
        UploadOptions options,
        Dictionary<int, DateTime> scheduledTimes,
        bool randomizeOrder,
        ScheduledUploadService scheduledUploadService)
    {
        var filesToSchedule = randomizeOrder
            ? filePaths.OrderBy(x => Guid.NewGuid()).ToList()
            : filePaths.ToList();
        
        Console.WriteLine($"=== 스케줄 등록 시작: {filesToSchedule.Count}개 파일");

        // 🔥 이 3줄 추가
        string currentUserId = _userId;
        string refreshToken = GetCurrentRefreshToken();
        Console.WriteLine($"=== UserId: {currentUserId}, RefreshToken 있음: {!string.IsNullOrEmpty(refreshToken)}");
        
        if (options.UseRandomInfo)
        {
            Console.WriteLine($"=== 랜덤 업로드 정보 활성화");
            Console.WriteLine($"    제목 풀: {options.RandomTitles?.Count ?? 0}개");
            Console.WriteLine($"    설명 풀: {options.RandomDescriptions?.Count ?? 0}개");
            Console.WriteLine($"    태그 풀: {options.RandomTags?.Count ?? 0}개");
            
            // 디버깅: 실제 내용 출력
            if (options.RandomTitles != null)
            {
                Console.WriteLine($"    제목 예시:");
                foreach (var t in options.RandomTitles.Take(3))
                {
                    Console.WriteLine($"      - {t.Substring(0, Math.Min(30, t.Length))}...");
                }
            }
        }
    
        for (int i = 0; i < filesToSchedule.Count; i++)
        {
            // 미리 계산된 시간 사용
            DateTime scheduledTime = scheduledTimes.ContainsKey(i) 
                ? scheduledTimes[i] 
                : DateTime.Now.AddMinutes(5 + (i * 10));
    
            string title, description, tags;
            
            if (options.UseRandomInfo)
            {
                // 각각 다른 시드로 진짜 랜덤 선택
                var titleRandom = new Random(Guid.NewGuid().GetHashCode());
                var descRandom = new Random(Guid.NewGuid().GetHashCode());
                var tagsRandom = new Random(Guid.NewGuid().GetHashCode());
                
                title = options.RandomTitles != null && options.RandomTitles.Count > 0
                    ? options.RandomTitles[titleRandom.Next(options.RandomTitles.Count)]
                    : (filesToSchedule.Count > 1 
                        ? options.TitleTemplate.Replace("#NUMBER", $"#{i + 1}")
                        : options.TitleTemplate.Replace(" #NUMBER", ""));
                
                description = options.RandomDescriptions != null && options.RandomDescriptions.Count > 0
                    ? options.RandomDescriptions[descRandom.Next(options.RandomDescriptions.Count)]
                    : options.Description;
                
                tags = options.RandomTags != null && options.RandomTags.Count > 0
                    ? options.RandomTags[tagsRandom.Next(options.RandomTags.Count)]
                    : options.Tags;
                
                Console.WriteLine($"=== 스케줄 {i + 1}/{filesToSchedule.Count}:");
                Console.WriteLine($"    제목: {title}");
                Console.WriteLine($"    설명: {description.Substring(0, Math.Min(50, description.Length))}...");
                Console.WriteLine($"    태그: {tags.Substring(0, Math.Min(30, tags.Length))}...");
                Console.WriteLine($"    예정: {scheduledTime:MM/dd HH:mm}");
            }
            else
            {
                title = filesToSchedule.Count > 1
                    ? options.TitleTemplate.Replace("#NUMBER", $"#{i + 1}")
                    : options.TitleTemplate.Replace(" #NUMBER", "");
                description = options.Description;
                tags = options.Tags;
            }
    
            var uploadItem = new ScheduledUploadItem
            {
                FileName = Path.GetFileName(filesToSchedule[i]),
                FilePath = filesToSchedule[i],
                ScheduledTime = scheduledTime,

                // 🔥 이 2줄 추가
                UserId = currentUserId,
                RefreshToken = refreshToken,
                
                Title = title,
                Description = description,
                Tags = tags,
                PrivacySetting = options.PrivacySetting
            };
    
            scheduledUploadService.AddScheduledUpload(_userId, uploadItem);
        }
        
        Console.WriteLine($"=== 스케줄 등록 완료: {filesToSchedule.Count}개");
    }
  
    /// <summary>
    /// 현재 Refresh Token 가져오기 (스케줄 업로드용)
    /// </summary>
    private string GetCurrentRefreshToken()
    {
        try
        {
            if (_youtubeUploader == null)
            {
                Console.WriteLine("=== YouTubeUploader가 null입니다");
                return "";
            }
                
            var credential = _youtubeUploader.GetCredential();
            if (credential?.Token?.RefreshToken == null)
            {
                Console.WriteLine("=== Refresh Token이 null입니다");
                return "";
            }
            
            Console.WriteLine("=== Refresh Token 추출 성공");
            return credential.Token.RefreshToken;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== Refresh Token 추출 실패: {ex.Message}");
            return "";
        }
    }
    
    /// <summary>
    /// 랜덤 업로드 시간 계산
    /// </summary>
    private DateTime CalculateRandomUploadTime(
        DateTime startTime,
        DateTime endTime,
        int index,
        int totalCount,
        int minIntervalMinutes)
    {
        var random = new Random();
        double totalMinutes = (endTime - startTime).TotalMinutes;
        double segmentMinutes = totalMinutes / totalCount;

        double segmentStart = index * segmentMinutes;
        double segmentEnd = Math.Min((index + 1) * segmentMinutes, totalMinutes);

        double randomMinutes = segmentStart + (random.NextDouble() * (segmentEnd - segmentStart));

        if (index > 0 && totalMinutes > (totalCount * minIntervalMinutes))
        {
            var previousTime = startTime.AddMinutes(segmentStart);
            var proposedTime = startTime.AddMinutes(randomMinutes);

            if ((proposedTime - previousTime).TotalMinutes < minIntervalMinutes)
            {
                randomMinutes = segmentStart + minIntervalMinutes;
            }
        }

        randomMinutes = Math.Min(randomMinutes, totalMinutes);
        return startTime.AddMinutes(randomMinutes);
    }

    /// <summary>
    /// 구독자 수 포맷
    /// </summary>
    public static string FormatSubscriberCount(ulong count)
    {
        if (count >= 1000000)
            return $"{count / 1000000.0:F1}M";
        else if (count >= 1000)
            return $"{count / 1000.0:F1}K";
        else
            return count.ToString();
    }

    /// <summary>
    /// 파일 크기 포맷
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        else if (bytes >= 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0:F2} MB";
        else if (bytes >= 1024)
            return $"{bytes / 1024.0:F2} KB";
        else
            return $"{bytes} bytes";
    }
 }
}
