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
private YouTubeUploader _youtubeUploader;
private YouTubeUploader.YouTubeAccountInfo _currentAccount;
  public YouTubeUploader.YouTubeAccountInfo CurrentAccount => _currentAccount;
    public bool IsAuthenticated => _currentAccount != null;

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

        _youtubeUploader = new YouTubeUploader();
        string currentUrl = await jsRuntime.InvokeAsync<string>("eval", "window.location.origin");
        return await _youtubeUploader.GetAuthorizationUrlAsync(currentUrl, returnPage);
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

            _youtubeUploader = new YouTubeUploader();
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
            _youtubeUploader = new YouTubeUploader();
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

        var uploadInfo = new YouTubeUploader.VideoUploadInfo
        {
            FilePath = filePath,
            Title = title,
            Description = options.Description,
            Tags = options.Tags,
            PrivacyStatus = options.PrivacySetting
        };

        return await _youtubeUploader.UploadVideoAsync(uploadInfo, progress);
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
  
              Console.WriteLine($"✅ 업로드 완료: {title} -> {videoUrl}");
          }
          catch (Exception ex)
          {
              Console.WriteLine($"❌ 업로드 실패 [{i + 1}]: {ex.Message}");
          }
      }
  
      return uploadedUrls;
  }
  
 
  
    /// <summary>
    /// 스케줄 업로드 등록
    /// </summary>
    public void RegisterScheduledUploads(
        List<string> filePaths,
        UploadOptions options,
        DateTime startTime,
        float scheduleHours,
        int minIntervalMinutes,
        bool randomizeOrder,
        ScheduledUploadService scheduledUploadService)
    {
        var random = new Random();
        var filesToSchedule = randomizeOrder
            ? filePaths.OrderBy(x => Guid.NewGuid()).ToList()
            : filePaths.ToList();
    
        DateTime endTime = startTime.AddHours(scheduleHours);
        
        Console.WriteLine($"=== 스케줄 등록 시작: {filesToSchedule.Count}개 파일");
        
        if (options.UseRandomInfo)
        {
            Console.WriteLine($"=== 랜덤 업로드 정보 활성화");
            Console.WriteLine($"    제목 풀: {options.RandomTitles?.Count ?? 0}개");
            Console.WriteLine($"    설명 풀: {options.RandomDescriptions?.Count ?? 0}개");
            Console.WriteLine($"    태그 풀: {options.RandomTags?.Count ?? 0}개");
            
            // 🔥 디버깅: 실제 내용 출력
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
            DateTime scheduledTime = CalculateRandomUploadTime(
                startTime, endTime, i, filesToSchedule.Count, minIntervalMinutes);
    
            string title, description, tags;
            
            if (options.UseRandomInfo)
            {
                // 🔥 완전히 랜덤하게 선택 (매번 새로운 인덱스)
                title = options.RandomTitles != null && options.RandomTitles.Count > 0
                    ? options.RandomTitles[random.Next(options.RandomTitles.Count)]
                    : (filesToSchedule.Count > 1 
                        ? options.TitleTemplate.Replace("#NUMBER", $"#{i + 1}")
                        : options.TitleTemplate.Replace(" #NUMBER", ""));
                
                description = options.RandomDescriptions != null && options.RandomDescriptions.Count > 0
                    ? options.RandomDescriptions[random.Next(options.RandomDescriptions.Count)]
                    : options.Description;
                
                tags = options.RandomTags != null && options.RandomTags.Count > 0
                    ? options.RandomTags[random.Next(options.RandomTags.Count)]
                    : options.Tags;
                
                Console.WriteLine($"=== 스케줄 {i + 1}/{filesToSchedule.Count}:");
                Console.WriteLine($"    제목: {title}");
                Console.WriteLine($"    설명: {description.Substring(0, Math.Min(50, description.Length))}...");
                Console.WriteLine($"    태그: {tags.Substring(0, Math.Min(30, tags.Length))}...");
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
                Title = title,
                Description = description,
                Tags = tags,
                PrivacySetting = options.PrivacySetting
            };
    
            scheduledUploadService.AddScheduledUpload(uploadItem);
        }
        
        Console.WriteLine($"=== 스케줄 등록 완료: {filesToSchedule.Count}개");
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
        DateTime startTime,
        float scheduleHours,
        int minIntervalMinutes,
        bool randomizeOrder,
        ScheduledUploadService scheduledUploadService)
    {
        var random = new Random();
        var filesToSchedule = randomizeOrder
            ? filePaths.OrderBy(x => Guid.NewGuid()).ToList()
            : filePaths.ToList();
    
        DateTime endTime = startTime.AddHours(scheduleHours);
        
        // 각 리스트 섞기
        List<string>? shuffledTitles = null;
        List<string>? shuffledDescriptions = null;
        List<string>? shuffledTags = null;
        
        if (options.UseRandomInfo)
        {
            if (options.RandomTitles != null && options.RandomTitles.Count > 0)
                shuffledTitles = options.RandomTitles.OrderBy(x => random.Next()).ToList();
            
            if (options.RandomDescriptions != null && options.RandomDescriptions.Count > 0)
                shuffledDescriptions = options.RandomDescriptions.OrderBy(x => random.Next()).ToList();
            
            if (options.RandomTags != null && options.RandomTags.Count > 0)
                shuffledTags = options.RandomTags.OrderBy(x => random.Next()).ToList();
        }
    
        for (int i = 0; i < filesToSchedule.Count; i++)
        {
            DateTime scheduledTime = CalculateRandomUploadTime(
                startTime, endTime, i, filesToSchedule.Count, minIntervalMinutes);
    
            string title, description, tags;
            
            if (options.UseRandomInfo)
            {
                // 🔥 각각 완전히 랜덤하게 선택
                title = shuffledTitles != null && shuffledTitles.Count > 0
                    ? shuffledTitles[random.Next(shuffledTitles.Count)]
                    : (filesToSchedule.Count > 1 
                        ? options.TitleTemplate.Replace("#NUMBER", $"#{i + 1}")
                        : options.TitleTemplate.Replace(" #NUMBER", ""));
                
                description = shuffledDescriptions != null && shuffledDescriptions.Count > 0
                    ? shuffledDescriptions[random.Next(shuffledDescriptions.Count)]
                    : options.Description;
                
                tags = shuffledTags != null && shuffledTags.Count > 0
                    ? shuffledTags[random.Next(shuffledTags.Count)]
                    : options.Tags;
                
                Console.WriteLine($"=== 스케줄 {i + 1}: 완전 랜덤 조합 - {title.Substring(0, Math.Min(30, title.Length))}...");
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
                Title = title,
                Description = description,
                Tags = tags,
                PrivacySetting = options.PrivacySetting
            };
    
            scheduledUploadService.AddScheduledUpload(uploadItem);
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

