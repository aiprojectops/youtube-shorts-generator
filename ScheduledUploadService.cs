using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Text.Json;

namespace YouTubeShortsWebApp
{
    public class ScheduledUploadService : BackgroundService
    {
        private readonly ILogger<ScheduledUploadService> _logger;
        private static readonly string QueueFilePath = Path.Combine(
            Path.GetTempPath(), 
            "YouTubeScheduledQueue.json"
        );

        // 🔥 파일 기반 영구 저장소
        private List<ScheduledUploadItem> _uploadQueue = new();
        private readonly object _queueLock = new object();

        public ScheduledUploadService(ILogger<ScheduledUploadService> logger)
        {
            _logger = logger;
            LoadQueueFromFile();
        }

        /// <summary>
        /// 파일에서 큐 로드
        /// </summary>
        private void LoadQueueFromFile()
        {
            try
            {
                if (File.Exists(QueueFilePath))
                {
                    string json = File.ReadAllText(QueueFilePath);
                    var items = JsonSerializer.Deserialize<List<ScheduledUploadItem>>(json);
                    
                    if (items != null)
                    {
                        lock (_queueLock)
                        {
                            _uploadQueue = items.Where(x => x.Status == "대기 중").ToList();
                        }
                        
                        Console.WriteLine($"=== 저장된 스케줄 복구: {_uploadQueue.Count}개");
                        _logger.LogInformation($"저장된 스케줄 복구: {_uploadQueue.Count}개");
                        
                        // 복구된 스케줄 출력
                        foreach (var item in _uploadQueue)
                        {
                            Console.WriteLine($"  - {item.FileName} -> {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("=== 저장된 스케줄 없음");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"큐 로드 실패: {ex.Message}");
                Console.WriteLine($"=== 큐 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 큐를 파일에 저장
        /// </summary>
        private void SaveQueueToFile()
        {
            try
            {
                lock (_queueLock)
                {
                    string json = JsonSerializer.Serialize(_uploadQueue, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                    File.WriteAllText(QueueFilePath, json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"큐 저장 실패: {ex.Message}");
                Console.WriteLine($"=== 큐 저장 실패: {ex.Message}");
            }
        }

        public void AddScheduledUpload(ScheduledUploadItem item)
        {
            lock (_queueLock)
            {
                _uploadQueue.Add(item);
            }
            
            SaveQueueToFile();
            
            _logger.LogInformation($"스케줄 추가: {item.FileName} at {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"=== ✅ 스케줄 추가: {item.FileName}");
            Console.WriteLine($"    예정 시간: {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"    현재 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"    남은 시간: {(item.ScheduledTime - DateTime.Now).TotalMinutes:F1}분");
        }

        public List<ScheduledUploadItem> GetAllScheduledItems()
        {
            lock (_queueLock)
            {
                return _uploadQueue.ToList();
            }
        }

        public int GetQueueCount()
        {
            lock (_queueLock)
            {
                return _uploadQueue.Count(x => x.Status == "대기 중");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 스케줄 업로드 서비스 시작됨");
            Console.WriteLine("=== 🚀 스케줄 업로드 서비스 시작됨");
            Console.WriteLine($"=== 현재 서버 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"=== 대기 중인 업로드: {GetQueueCount()}개");
        
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var itemsToUpload = new List<ScheduledUploadItem>();
        
                    lock (_queueLock)
                    {
                        itemsToUpload = _uploadQueue
                            .Where(x => x.ScheduledTime <= now && x.Status == "대기 중")
                            .ToList();
                    }
        
                    if (itemsToUpload.Any())
                    {
                        Console.WriteLine($"=== ⏰ {now:yyyy-MM-dd HH:mm:ss} - 업로드 대상 발견: {itemsToUpload.Count}개");
                        
                        foreach (var item in itemsToUpload)
                        {
                            Console.WriteLine($"    📤 {item.FileName}");
                            Console.WriteLine($"       예정: {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
                            Console.WriteLine($"       실제: {now:yyyy-MM-dd HH:mm:ss}");
                            Console.WriteLine($"       지연: {(now - item.ScheduledTime).TotalMinutes:F1}분");
                        }
                    }
        
                    foreach (var item in itemsToUpload)
                    {
                        try
                        {
                            await ProcessUpload(item);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"업로드 실패: {item.FileName} - {ex.Message}");
                            Console.WriteLine($"=== ❌ 업로드 실패: {item.FileName}");
                            Console.WriteLine($"    오류: {ex.Message}");
                            
                            item.Status = "실패";
                            item.ErrorMessage = ex.Message;
                            item.CompletedTime = DateTime.Now;
                        }
                    }
        
                    // 완료된 항목 제거 및 저장
                    if (itemsToUpload.Any())
                    {
                        lock (_queueLock)
                        {
                            _uploadQueue.RemoveAll(x => x.Status != "대기 중");
                        }
                        SaveQueueToFile();
                        
                        int remainingCount = GetQueueCount();
                        Console.WriteLine($"=== 📊 남은 업로드: {remainingCount}개");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"스케줄 서비스 오류: {ex.Message}");
                    Console.WriteLine($"=== ⚠️ 스케줄 서비스 오류: {ex.Message}");
                }
        
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        
            _logger.LogInformation("🛑 스케줄 업로드 서비스 종료됨");
            Console.WriteLine("=== 🛑 스케줄 업로드 서비스 종료됨");
        }

        private async Task ProcessUpload(ScheduledUploadItem item)
        {
            var startTime = DateTime.Now;
            
            _logger.LogInformation($"⬆️ 업로드 시작: {item.FileName}");
            Console.WriteLine($"=== ⬆️ 업로드 시작: {item.FileName}");
            Console.WriteLine($"    제목: {item.Title}");
            Console.WriteLine($"    시작 시간: {startTime:yyyy-MM-dd HH:mm:ss}");

            item.Status = "업로드 중";
            item.StartTime = startTime;
            SaveQueueToFile();

            try
            {
                // YouTube 업로더 생성 및 인증
                var youtubeUploader = new YouTubeUploader();

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

                // 진행률 추적
                var progress = new Progress<YouTubeUploader.UploadProgressInfo>(progressInfo =>
                {
                    if (progressInfo.Percentage % 25 == 0) // 25%마다 로그
                    {
                        Console.WriteLine($"    진행률: {progressInfo.Percentage}% - {progressInfo.Status}");
                    }
                });

                // YouTube 업로드 실행
                string videoUrl = await youtubeUploader.UploadVideoAsync(uploadInfo, progress);

                // 업로드 완료 처리
                var completedTime = DateTime.Now;
                var duration = completedTime - startTime;
                
                item.Status = "완료";
                item.UploadedUrl = videoUrl;
                item.CompletedTime = completedTime;

                _logger.LogInformation($"✅ 업로드 완료: {item.FileName} -> {videoUrl}");
                Console.WriteLine($"=== ✅ 업로드 완료: {item.FileName}");
                Console.WriteLine($"    제목: {item.Title}");
                Console.WriteLine($"    URL: {videoUrl}");
                Console.WriteLine($"    완료 시간: {completedTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"    소요 시간: {duration.TotalMinutes:F1}분");
                Console.WriteLine($"    예정 시간: {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");

                // 리소스 정리
                youtubeUploader.Dispose();
            }
            catch (Exception ex)
            {
                item.Status = "실패";
                item.ErrorMessage = ex.Message;
                item.CompletedTime = DateTime.Now;

                _logger.LogError($"❌ 업로드 실패: {item.FileName} - {ex.Message}");
                Console.WriteLine($"=== ❌ 업로드 실패: {item.FileName}");
                Console.WriteLine($"    오류: {ex.Message}");

                throw;
            }
            finally
            {
                // 임시 파일 삭제
                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        File.Delete(item.FilePath);
                        Console.WriteLine($"    🗑️ 임시 파일 삭제: {Path.GetFileName(item.FilePath)}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"임시 파일 삭제 실패: {item.FilePath} - {ex.Message}");
                    Console.WriteLine($"    ⚠️ 임시 파일 삭제 실패: {ex.Message}");
                }
                
                SaveQueueToFile();
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
        public string Status { get; set; } = "대기 중";
        public string? UploadedUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? CompletedTime { get; set; }
    }
}
