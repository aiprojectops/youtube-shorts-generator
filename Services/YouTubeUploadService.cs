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


    // 기존 UploadOptions 클래스에 속성 추가
    public class UploadOptions
    {
        public string TitleTemplate { get; set; } = "";
        public string Description { get; set; } = "";
        public string Tags { get; set; } = "";
        public string PrivacySetting { get; set; } = "공개";
        
        // 🔥 랜덤 업로드 정보 추가
        public bool UseRandomInfo { get; set; } = false;
        public List<RandomUploadInfo>? RandomInfoList { get; set; } = null;
    }
    
    // 🔥 새로운 클래스 추가
    public class RandomUploadInfo
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Tags { get; set; } = "";
    }
  
    /// <summary>
    /// 여러 파일 즉시 업로드
    /// </summary>
    // 🔥 UploadMultipleVideosAsync 메서드 수정
    public async Task<List<string>> UploadMultipleVideosAsync(
        List<string> filePaths,
        UploadOptions options,
        Action<int, int, string>? progressCallback = null)
    {
        var uploadedUrls = new List<string>();
        var random = new Random();
        
        // 🔥 랜덤 정보가 있으면 섞기
        List<RandomUploadInfo>? shuffledInfoList = null;
        if (options.UseRandomInfo && options.RandomInfoList != null && options.RandomInfoList.Count > 0)
        {
            shuffledInfoList = options.RandomInfoList.OrderBy(x => random.Next()).ToList();
            Console.WriteLine($"=== 랜덤 업로드 정보 사용: {shuffledInfoList.Count}개");
        }
    
        for (int i = 0; i < filePaths.Count; i++)
        {
            try
            {
                string filePath = filePaths[i];
                
                // 🔥 랜덤 정보 또는 기본 정보 사용
                string title, description, tags;
                
                if (shuffledInfoList != null && shuffledInfoList.Count > 0)
                {
                    // 랜덤 정보에서 순환하며 선택
                    var info = shuffledInfoList[i % shuffledInfoList.Count];
                    title = info.Title;
                    description = info.Description;
                    tags = info.Tags;
                    
                    Console.WriteLine($"=== 영상 {i + 1}: 랜덤 정보 사용");
                    Console.WriteLine($"    제목: {title}");
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
        var filesToSchedule = randomizeOrder
            ? filePaths.OrderBy(x => Guid.NewGuid()).ToList()
            : filePaths.ToList();

        DateTime endTime = startTime.AddHours(scheduleHours);

        for (int i = 0; i < filesToSchedule.Count; i++)
        {
            DateTime scheduledTime = CalculateRandomUploadTime(
                startTime, endTime, i, filesToSchedule.Count, minIntervalMinutes);

            string title = filesToSchedule.Count > 1
                ? options.TitleTemplate.Replace("#NUMBER", $"#{i + 1}")
                : options.TitleTemplate.Replace(" #NUMBER", "");

            var uploadItem = new ScheduledUploadItem
            {
                FileName = Path.GetFileName(filesToSchedule[i]),
                FilePath = filesToSchedule[i],
                ScheduledTime = scheduledTime,
                Title = title,
                Description = options.Description,
                Tags = options.Tags,
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

