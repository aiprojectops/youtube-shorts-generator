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
                    var itemsToProcess = new List<ScheduledUploadItem>();
        
                    lock (_queueLock)
                    {
                        // 🔥 5분 이내에 업로드해야 할 항목 찾기
                        itemsToProcess = _uploadQueue
                            .Where(x => x.ScheduledTime <= now.AddMinutes(5) && x.Status == "대기 중")
                            .ToList();
                    }
        
                    if (itemsToProcess.Any())
                    {
                        Console.WriteLine($"=== ⏰ {now:yyyy-MM-dd HH:mm:ss} - 처리 대상 발견: {itemsToProcess.Count}개");
                        
                        foreach (var item in itemsToProcess)
                        {
                            // 생성이 필요한 항목은 생성 시작
                            if (item.NeedsGeneration && string.IsNullOrEmpty(item.FilePath))
                            {
                                var timeUntilUpload = item.ScheduledTime - now;
                                if (timeUntilUpload.TotalMinutes <= 5)
                                {
                                    Console.WriteLine($"    🎬 {item.FileName} 영상 생성 시작");
                                    item.Status = "생성 중";
                                    SaveQueueToFile();
                                    
                                    try
                                    {
                                        await GenerateVideoForUpload(item);
                                        item.Status = "생성 완료";
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"    ❌ 영상 생성 실패: {ex.Message}");
                                        item.Status = "생성 실패";
                                        item.ErrorMessage = ex.Message;
                                        SaveQueueToFile();
                                        continue;
                                    }
                                }
                            }
                            
                            // 업로드 시간이 되면 업로드
                            if (item.ScheduledTime <= now && !string.IsNullOrEmpty(item.FilePath))
                            {
                                Console.WriteLine($"    📤 {item.FileName}");
                                Console.WriteLine($"       예정: {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
                                Console.WriteLine($"       실제: {now:yyyy-MM-dd HH:mm:ss}");
                                
                                try
                                {
                                    await ProcessUpload(item);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"업로드 실패: {item.FileName} - {ex.Message}");
                                    Console.WriteLine($"=== ❌ 업로드 실패: {item.FileName}");
                                    item.Status = "실패";
                                    item.ErrorMessage = ex.Message;
                                    item.CompletedTime = DateTime.Now;
                                }
                            }
                        }
        
                        // 완료된 항목 제거
                        lock (_queueLock)
                        {
                            _uploadQueue.RemoveAll(x => x.Status != "대기 중" && x.Status != "생성 중" && x.Status != "생성 완료");
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


        private async Task GenerateVideoForUpload(ScheduledUploadItem item)
        {
            Console.WriteLine($"=== 영상 생성 시작: {item.FileName}");
            
            var config = ConfigManager.GetConfig();
            var replicateClient = new ReplicateClient(config.ReplicateApiKey);
            
            try
            {
                // 🔥 ReplicateClient의 실제 메서드 사용
                var request = new ReplicateClient.VideoGenerationRequest
                {
                    prompt = item.Prompt ?? "",
                    duration = item.Duration,
                    aspect_ratio = item.AspectRatio,
                    resolution = "1080p",
                    fps = 24,
                    camera_fixed = true
                };
                
                // 영상 생성 시작
                var prediction = await replicateClient.StartVideoGeneration(request);
                Console.WriteLine($"=== 영상 생성 요청 완료. Prediction ID: {prediction.id}");
                
                // 완료 대기
                var progress = new Progress<ReplicateClient.ProgressInfo>(info =>
                {
                    Console.WriteLine($"    진행률: {info.Percentage}% - {info.Status}");
                });
                
                var completedPrediction = await replicateClient.WaitForCompletion(
                    prediction.id, 
                    progress, 
                    CancellationToken.None
                );
                
                // 결과 URL 추출
                string videoUrl = "";
                if (completedPrediction.output != null)
                {
                    if (completedPrediction.output is string urlString)
                    {
                        videoUrl = urlString;
                    }
                    else if (completedPrediction.output is Newtonsoft.Json.Linq.JArray array && array.Count > 0)
                    {
                        videoUrl = array[0].ToString();
                    }
                }
                
                if (string.IsNullOrEmpty(videoUrl))
                {
                    throw new Exception("영상 URL을 가져올 수 없습니다.");
                }
                
                Console.WriteLine($"=== 영상 생성 완료: {videoUrl}");
                
                // 다운로드
                string tempDir = Path.Combine(Path.GetTempPath(), "YouTubeScheduledUploads");
                Directory.CreateDirectory(tempDir);
                string videoPath = Path.Combine(tempDir, $"{DateTime.Now.Ticks}_{item.FileName}");
                
                using (var httpClient = new HttpClient())
                {
                    var videoBytes = await httpClient.GetByteArrayAsync(videoUrl);
                    await File.WriteAllBytesAsync(videoPath, videoBytes);
                }
                
                Console.WriteLine($"=== 영상 다운로드 완료: {videoPath}");
                
                // 후처리
                if (item.EnablePostProcessing)
                {
                    Console.WriteLine($"=== 후처리 시작: {item.FileName}");
                    
                    string processedPath = videoPath.Replace(".mp4", "_processed.mp4");
                    
                    // 🔥 ProcessingOptions 객체 생성
                    var processingOptions = new VideoPostProcessor.ProcessingOptions
                    {
                        InputVideoPath = videoPath,
                        OutputVideoPath = processedPath,
                        CaptionText = item.CaptionText ?? "",
                        FontSize = item.CaptionSize ?? "80",
                        FontColor = item.CaptionColor ?? "white",
                        CaptionPosition = item.CaptionPosition ?? "bottom",
                        BackgroundMusicPath = item.MusicFilePath ?? "",
                        MusicVolume = item.MusicVolume
                    };
                    
                    // ProcessVideoAsync 호출
                    string finalPath = await VideoPostProcessor.ProcessVideoAsync(processingOptions);
                    
                    // 원본 삭제
                    try
                    {
                        if (File.Exists(videoPath))
                        {
                            File.Delete(videoPath);
                        }
                        videoPath = finalPath;
                        Console.WriteLine($"=== 후처리 완료: {finalPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"=== 원본 파일 삭제 실패: {ex.Message}");
                        videoPath = finalPath;
                    }
                }
                
                item.FilePath = videoPath;
                SaveQueueToFile();
                
                Console.WriteLine($"=== ✅ 영상 준비 완료: {item.FileName}");
            }
            finally
            {
                replicateClient.Dispose();
            }
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

        // 🔥 영상 생성 정보 추가
        public bool NeedsGeneration { get; set; } = false;
        public string? Prompt { get; set; }
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
}
