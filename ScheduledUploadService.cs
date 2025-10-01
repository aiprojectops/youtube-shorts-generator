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
                        // 🔥 처리 대상 찾기: 생성이 필요한 것, 업로드할 것
                        itemsToProcess = _uploadQueue
                            .Where(x => 
                                (x.Status == "대기 중" || x.Status == "생성 완료") &&
                                x.ScheduledTime <= now.AddMinutes(5))
                            .OrderBy(x => x.ScheduledTime)
                            .ToList();
                    }

                    if (itemsToProcess.Any())
                    {
                        Console.WriteLine($"=== ⏰ {now:yyyy-MM-dd HH:mm:ss} - 처리 대상 발견: {itemsToProcess.Count}개");
                        
                        foreach (var item in itemsToProcess)
                        {
                            try
                            {
                                // 🔥 1단계: 생성이 필요한 경우
                                if (item.NeedsGeneration && string.IsNullOrEmpty(item.FilePath))
                                {
                                    var timeUntilUpload = item.ScheduledTime - now;
                                    
                                    // 업로드 5분 전이면 생성 시작
                                    if (timeUntilUpload.TotalMinutes <= 5 && item.Status == "대기 중")
                                    {
                                        Console.WriteLine($"    🎬 {item.FileName} 영상 생성 시작");
                                        Console.WriteLine($"       업로드 예정: {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
                                        
                                        item.Status = "생성 중";
                                        SaveQueueToFile();
                                        
                                        try
                                        {
                                            await GenerateVideoForUpload(item);
                                            // GenerateVideoForUpload에서 이미 Status를 "생성 완료"로 변경함
                                            Console.WriteLine($"    ✅ {item.FileName} 생성 완료");
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
                                
                                // 🔥 2단계: 업로드 시간이 되면 업로드
                                if (item.ScheduledTime <= now && 
                                    !string.IsNullOrEmpty(item.FilePath) && 
                                    File.Exists(item.FilePath) &&
                                    item.Status == "생성 완료")
                                {
                                    Console.WriteLine($"    📤 {item.FileName} 업로드 시작");
                                    Console.WriteLine($"       예정: {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
                                    Console.WriteLine($"       실제: {now:yyyy-MM-dd HH:mm:ss}");
                                    Console.WriteLine($"       파일 경로: {item.FilePath}");
                                    Console.WriteLine($"       파일 존재: {File.Exists(item.FilePath)}");
                                    
                                    try
                                    {
                                        await ProcessUpload(item);
                                        Console.WriteLine($"    ✅ {item.FileName} 업로드 완료");
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError($"업로드 실패: {item.FileName} - {ex.Message}");
                                        Console.WriteLine($"=== ❌ 업로드 실패: {item.FileName}");
                                        Console.WriteLine($"    오류: {ex.Message}");
                                        item.Status = "실패";
                                        item.ErrorMessage = ex.Message;
                                        item.CompletedTime = DateTime.Now;
                                        SaveQueueToFile();
                                    }
                                }
                            }
                            catch (Exception itemEx)
                            {
                                Console.WriteLine($"=== ❌ 항목 처리 오류: {item.FileName}");
                                Console.WriteLine($"    오류: {itemEx.Message}");
                                item.Status = "오류";
                                item.ErrorMessage = itemEx.Message;
                                SaveQueueToFile();
                            }
                        }

                        // 완료된 항목 제거
                        lock (_queueLock)
                        {
                            int beforeCount = _uploadQueue.Count;
                            _uploadQueue.RemoveAll(x => 
                                x.Status == "완료" || 
                                x.Status == "실패" || 
                                x.Status == "오류" ||
                                x.Status == "생성 실패");
                            int afterCount = _uploadQueue.Count;
                            
                            if (beforeCount != afterCount)
                            {
                                Console.WriteLine($"=== 🗑️ 완료/실패 항목 제거: {beforeCount - afterCount}개");
                            }
                        }
                        SaveQueueToFile();
                        
                        int remainingCount = GetQueueCount();
                        Console.WriteLine($"=== 📊 남은 대기 항목: {remainingCount}개");
                        
                        // 남은 항목 상태 출력
                        lock (_queueLock)
                        {
                            var remaining = _uploadQueue.Where(x => x.Status == "대기 중" || x.Status == "생성 완료").ToList();
                            foreach (var item in remaining.Take(3))
                            {
                                Console.WriteLine($"    - {item.FileName}: {item.Status} (예정: {item.ScheduledTime:MM/dd HH:mm})");
                            }
                        }
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
            Console.WriteLine($"    프롬프트: {item.Prompt}");
            Console.WriteLine($"    시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            
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
                    Console.WriteLine($"    생성 진행률: {info.Percentage}% - {info.Status}");
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
                
                // 🔥 파일명을 item.FileName으로 사용
                string videoPath = Path.Combine(tempDir, item.FileName);
                
                Console.WriteLine($"=== 다운로드 시작: {videoPath}");
                
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(10);
                    var videoBytes = await httpClient.GetByteArrayAsync(videoUrl);
                    await File.WriteAllBytesAsync(videoPath, videoBytes);
                }
                
                Console.WriteLine($"=== 영상 다운로드 완료: {videoPath}");
                Console.WriteLine($"=== 파일 크기: {new FileInfo(videoPath).Length / 1024 / 1024} MB");
                
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
                
                // 🔥 FilePath 업데이트 및 상태 변경
                item.FilePath = videoPath;
                item.Status = "생성 완료";
                SaveQueueToFile();
                
                Console.WriteLine($"=== ✅ 영상 준비 완료: {item.FileName}");
                Console.WriteLine($"=== 저장된 경로: {videoPath}");
                Console.WriteLine($"=== 파일 존재 확인: {File.Exists(videoPath)}");
                
                // 🔥 히스토리 저장 추가
                try
                {
                    var historyItem = new VideoHistoryManager.VideoHistoryItem
                    {
                        Prompt = item.Prompt ?? "",
                        FinalPrompt = item.Prompt ?? "",
                        Duration = item.Duration,
                        AspectRatio = item.AspectRatio,
                        VideoUrl = videoUrl,
                        IsRandomPrompt = true,
                        FileName = item.FileName,
                        IsDownloaded = false,
                        IsUploaded = false,
                        Status = item.EnablePostProcessing ? "후처리 완료" : "생성 완료"
                    };
                    
                    VideoHistoryManager.AddHistoryItem(historyItem);
                    Console.WriteLine($"=== ✅ 히스토리에 저장됨: {item.FileName}");
                }
                catch (Exception historyEx)
                {
                    Console.WriteLine($"=== ⚠️ 히스토리 저장 실패: {historyEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== ❌ 영상 생성 실패: {ex.Message}");
                item.Status = "생성 실패";
                item.ErrorMessage = ex.Message;
                SaveQueueToFile();
                throw;
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
            Console.WriteLine($"    파일 경로: {item.FilePath}");
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

                // 🔥 히스토리 업데이트
                try
                {
                    var historyItems = VideoHistoryManager.GetHistory();
                    var historyItem = historyItems.FirstOrDefault(h => h.FileName == item.FileName);
                    
                    if (historyItem != null)
                    {
                        VideoHistoryManager.UpdateHistoryItem(historyItem.Id, h =>
                        {
                            h.IsUploaded = true;
                            h.YouTubeUrl = videoUrl;
                            h.Status = "업로드 완료";
                        });
                        Console.WriteLine($"=== ✅ 히스토리 업데이트됨: {item.FileName}");
                    }
                    else
                    {
                        Console.WriteLine($"=== ⚠️ 히스토리에서 찾을 수 없음: {item.FileName}");
                    }
                }
                catch (Exception historyEx)
                {
                    Console.WriteLine($"=== ⚠️ 히스토리 업데이트 실패: {historyEx.Message}");
                }

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
