using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace YouTubeShortsWebApp
{
    public class ScheduledUploadService : BackgroundService
    {
        private readonly ConcurrentQueue<ScheduledUploadItem> _uploadQueue = new();
        private readonly ILogger<ScheduledUploadService> _logger;

        public ScheduledUploadService(ILogger<ScheduledUploadService> logger)
        {
            _logger = logger;
        }

        public void AddScheduledUpload(ScheduledUploadItem item)
        {
            _uploadQueue.Enqueue(item);
            _logger.LogInformation($"스케줄 업로드 추가: {item.FileName} at {item.ScheduledTime}");
            Console.WriteLine($"=== 스케줄 추가: {item.FileName} -> {item.ScheduledTime}");
        }

        public List<ScheduledUploadItem> GetAllScheduledItems()
        {
            return _uploadQueue.ToList();
        }

        public int GetQueueCount()
        {
            return _uploadQueue.Count;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("스케줄 업로드 서비스 시작됨");
            Console.WriteLine("=== 스케줄 업로드 서비스 시작됨");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var itemsToUpload = new List<ScheduledUploadItem>();

                    // 현재 시간에 업로드해야 할 항목들 찾기
                    var tempQueue = new List<ScheduledUploadItem>();
                    while (_uploadQueue.TryDequeue(out var item))
                    {
                        if (item.ScheduledTime <= now && item.Status == "대기 중")
                        {
                            itemsToUpload.Add(item);
                            Console.WriteLine($"=== 업로드 대상 발견: {item.FileName}");
                        }
                        else if (item.Status == "대기 중")
                        {
                            tempQueue.Add(item); // 아직 시간이 안된 것들은 다시 큐에
                        }
                        // 완료되거나 실패한 것들은 큐에서 제거
                    }

                    // 다시 큐에 넣기
                    foreach (var item in tempQueue)
                    {
                        _uploadQueue.Enqueue(item);
                    }

                    // 업로드 실행
                    foreach (var item in itemsToUpload)
                    {
                        try
                        {
                            await ProcessUpload(item);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"업로드 실패: {item.FileName} - {ex.Message}");
                            Console.WriteLine($"=== 업로드 실패: {item.FileName} - {ex.Message}");
                            item.Status = "실패";
                            item.ErrorMessage = ex.Message;
                        }
                    }

                    // 큐 상태 로깅
                    if (_uploadQueue.Count > 0)
                    {
                        Console.WriteLine($"=== 대기 중인 업로드: {_uploadQueue.Count}개");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"스케줄 서비스 오류: {ex.Message}");
                    Console.WriteLine($"=== 스케줄 서비스 오류: {ex.Message}");
                }

                // 1분마다 체크
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("스케줄 업로드 서비스 종료됨");
            Console.WriteLine("=== 스케줄 업로드 서비스 종료됨");
        }

        private async Task ProcessUpload(ScheduledUploadItem item)
        {
            _logger.LogInformation($"업로드 시작: {item.FileName}");
            Console.WriteLine($"=== 업로드 시작: {item.FileName}");

            item.Status = "업로드 중";
            item.StartTime = DateTime.Now;

            try
            {
                // YouTube 업로더 생성 및 인증
                var youtubeUploader = new YouTubeUploader();

                // 기존 인증 정보 사용 (이미 인증된 상태라고 가정)
                bool authSuccess = await youtubeUploader.AuthenticateAsync();
                if (!authSuccess)
                {
                    throw new Exception("YouTube 인증 실패");
                }

                // 업로드 정보 준비
                var uploadInfo = new YouTubeUploader.VideoUploadInfo
                {
                    FilePath = item.FilePath,
                    Title = item.Title,
                    Description = item.Description,
                    Tags = item.Tags,
                    PrivacyStatus = item.PrivacySetting
                };

                // 진행률 추적 (로그용)
                var progress = new Progress<YouTubeUploader.UploadProgressInfo>(progressInfo =>
                {
                    Console.WriteLine($"=== {item.FileName} 업로드 진행률: {progressInfo.Percentage}% - {progressInfo.Status}");
                });

                // YouTube 업로드 실행
                string videoUrl = await youtubeUploader.UploadVideoAsync(uploadInfo, progress);

                // 업로드 완료 처리
                item.Status = "완료";
                item.UploadedUrl = videoUrl;
                item.CompletedTime = DateTime.Now;

                _logger.LogInformation($"업로드 완료: {item.FileName} -> {videoUrl}");
                Console.WriteLine($"=== 업로드 완료: {item.FileName} -> {videoUrl}");

                // 리소스 정리
                youtubeUploader.Dispose();
            }
            catch (Exception ex)
            {
                item.Status = "실패";
                item.ErrorMessage = ex.Message;
                item.CompletedTime = DateTime.Now;

                _logger.LogError($"업로드 실패: {item.FileName} - {ex.Message}");
                Console.WriteLine($"=== 업로드 실패: {item.FileName} - {ex.Message}");

                throw; // 상위로 예외 전파
            }
            finally
            {
                // 임시 파일 삭제
                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        File.Delete(item.FilePath);
                        Console.WriteLine($"=== 임시 파일 삭제: {item.FilePath}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"임시 파일 삭제 실패: {item.FilePath} - {ex.Message}");
                    Console.WriteLine($"=== 임시 파일 삭제 실패: {item.FilePath} - {ex.Message}");
                }
            }
        }
    }

    public class ScheduledUploadItem
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime ScheduledTime { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Tags { get; set; } = "";
        public string PrivacySetting { get; set; } = "";
        public string Status { get; set; } = "대기 중"; // 대기 중, 업로드 중, 완료, 실패
        public string? UploadedUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? CompletedTime { get; set; }
    }
}