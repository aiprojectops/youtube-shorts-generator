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

        // 파일 기반 영구 저장소
        private List<ScheduledUploadItem> _uploadQueue = new();
        private readonly object _queueLock = new object();

        // 완료 이벤트 추가
        public event Action<int, int, List<ScheduledUploadItem>>? OnAllUploadsCompleted;
        
        private int _currentBatchTotal = 0;
        private int _currentBatchCompleted = 0;
        private int _currentBatchSuccess = 0;
        private DateTime _batchStartTime = DateTime.MinValue;

        public ScheduledUploadService(ILogger<ScheduledUploadService> logger)
        {
            _logger = logger;
            LoadQueueFromFile();
        }

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
                            _uploadQueue = items.Where(x => x.Status == "대기 중" || x.Status == "생성 완료").ToList();
                        }
                        
                        Console.WriteLine($"=== 저장된 스케줄 복구: {_uploadQueue.Count}개");
                        _logger.LogInformation($"저장된 스케줄 복구: {_uploadQueue.Count}개");
                        
                        foreach (var item in _uploadQueue)
                        {
                            Console.WriteLine($"  - {item.FileName} -> {item.ScheduledTime:yyyy-MM-dd HH:mm:ss} (상태: {item.Status})");
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
                
                // 새로운 배치 시작
                if (_batchStartTime == DateTime.MinValue)
                {
                    _batchStartTime = DateTime.Now;
                    _currentBatchTotal = 0;
                    _currentBatchCompleted = 0;
                    _currentBatchSuccess = 0;
                }
                _currentBatchTotal++;
            }
            
            SaveQueueToFile();
            
            _logger.LogInformation($"스케줄 추가: {item.FileName} at {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"=== ✅ 스케줄 추가: {item.FileName}");
            Console.WriteLine($"    예정 시간: {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"    현재 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"    남은 시간: {(item.ScheduledTime - DateTime.Now).TotalMinutes:F1}분");
            Console.WriteLine($"    배치 총 개수: {_currentBatchTotal}개");
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
                return _uploadQueue.Count(x => x.Status == "대기 중" || x.Status == "생성 완료");
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
                                // 1단계: 생성이 필요한 경우
                                if (item.NeedsGeneration && string.IsNullOrEmpty(item.FilePath))
                                {
                                    var timeUntilUpload = item.ScheduledTime - now;
                                    
                                    if (timeUntilUpload.TotalMinutes <= 5 && item.Status == "대기 중")
                                    {
                                        Console.WriteLine($"    🎬 {item.FileName} 영상 생성 시작");
                                        Console.WriteLine($"       업로드 예정: {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
                                        
                                        item.Status = "생성 중";
                                        SaveQueueToFile();
                                        
                                        try
                                        {
                                            await GenerateVideoForUpload(item);
                                            Console.WriteLine($"    ✅ {item.FileName} 생성 완료");
                                            _logger.LogInformation($"[영상 생성 완료] {item.FileName}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"    ❌ 영상 생성 실패: {ex.Message}");
                                            _logger.LogError($"[영상 생성 실패] {item.FileName}: {ex.Message}");
                                            item.Status = "생성 실패";
                                            item.ErrorMessage = ex.Message;
                                            SaveQueueToFile();
                                            
                                            _currentBatchCompleted++;
                                            CheckBatchCompletion();
                                            continue;
                                        }
                                    }
                                }
                                
                                // 2단계: 업로드 시간이 되면 업로드
                                if (item.ScheduledTime <= now && 
                                    !string.IsNullOrEmpty(item.FilePath) && 
                                    File.Exists(item.FilePath) &&
                                    item.Status == "생성 완료")
                                {
                                    Console.WriteLine($"    📤 {item.FileName} 업로드 시작");
                                    Console.WriteLine($"       예정: {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
                                    Console.WriteLine($"       실제: {now:yyyy-MM-dd HH:mm:ss}");
                                    _logger.LogInformation($"[업로드 시작] {item.FileName} - 예정: {item.ScheduledTime:HH:mm:ss}, 실제: {now:HH:mm:ss}");
                                    
                                    try
                                    {
                                        await ProcessUpload(item);
                                        Console.WriteLine($"    ✅ {item.FileName} 업로드 완료");
                                        _logger.LogInformation($"[업로드 완료] {item.FileName} -> {item.UploadedUrl}");
                                        
                                        _currentBatchCompleted++;
                                        _currentBatchSuccess++;
                                        CheckBatchCompletion();
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError($"[업로드 실패] {item.FileName}: {ex.Message}");
                                        Console.WriteLine($"=== ❌ 업로드 실패: {item.FileName}");
                                        Console.WriteLine($"    오류: {ex.Message}");
                                        item.Status = "실패";
                                        item.ErrorMessage = ex.Message;
                                        item.CompletedTime = DateTime.Now;
                                        SaveQueueToFile();
                                        
                                        _currentBatchCompleted++;
                                        CheckBatchCompletion();
                                    }
                                }
                            }
                            catch (Exception itemEx)
                            {
                                Console.WriteLine($"=== ❌ 항목 처리 오류: {item.FileName}");
                                Console.WriteLine($"    오류: {itemEx.Message}");
                                _logger.LogError($"[항목 처리 오류] {item.FileName}: {itemEx.Message}");
                                item.Status = "오류";
                                item.ErrorMessage = itemEx.Message;
                                SaveQueueToFile();
                                
                                _currentBatchCompleted++;
                                CheckBatchCompletion();
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

        private void CheckBatchCompletion()
        {
            if (_currentBatchTotal > 0 && _currentBatchCompleted >= _currentBatchTotal)
            {
                var duration = DateTime.Now - _batchStartTime;
                
                Console.WriteLine("");
                Console.WriteLine("===========================================");
                Console.WriteLine("🎉 전체 스케줄 업로드 배치 완료!");
                Console.WriteLine("===========================================");
                Console.WriteLine($"총 파일 수: {_currentBatchTotal}개");
                Console.WriteLine($"성공: {_currentBatchSuccess}개");
                Console.WriteLine($"실패: {_currentBatchTotal - _currentBatchSuccess}개");
                Console.WriteLine($"총 소요 시간: {duration.TotalMinutes:F1}분");
                Console.WriteLine($"시작 시간: {_batchStartTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"완료 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine("===========================================");
                Console.WriteLine("");

                _logger.LogInformation($"[배치 완료] 성공: {_currentBatchSuccess}/{_currentBatchTotal}, 소요: {duration.TotalMinutes:F1}분");

                // 완료된 항목 리스트 가져오기
                List<ScheduledUploadItem> completedItems;
                lock (_queueLock)
                {
                    completedItems = _uploadQueue
                        .Where(x => x.Status == "완료" || x.Status == "실패" || x.Status == "오류" || x.Status == "생성 실패")
                        .ToList();
                }

                // 이벤트 발생
                OnAllUploadsCompleted?.Invoke(_currentBatchSuccess, _currentBatchTotal, completedItems);

                // 배치 카운터 리셋
                _batchStartTime = DateTime.MinValue;
                _currentBatchTotal = 0;
                _currentBatchCompleted = 0;
                _currentBatchSuccess = 0;
            }
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
                var request = new ReplicateClient.VideoGenerationRequest
                {
                    prompt = item.Prompt ?? "",
                    duration = item.Duration,
                    aspect_ratio = item.AspectRatio,
                    resolution = "1080p",
                    fps = 24,
                    camera_fixed = true
                };
                
                var prediction = await replicateClient.StartVideoGeneration(request);
                Console.WriteLine($"=== 영상 생성 요청 완료. Prediction ID: {prediction.id}");
                
                var progress = new Progress<ReplicateClient.ProgressInfo>(info =>
                {
                    Console.WriteLine($"    생성 진행률: {info.Percentage}% - {info.Status}");
                });
                
                var completedPrediction = await replicateClient.WaitForCompletion(
                    prediction.id, 
                    progress, 
                    CancellationToken.None
                );
                
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
                
                string tempDir = Path.Combine(Path.GetTempPath(), "YouTubeScheduledUploads");
                Directory.CreateDirectory(tempDir);
                
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
                
                if (item.EnablePostProcessing)
                {
                    Console.WriteLine($"=== 후처리 시작: {item.FileName}");
                    
                    string processedPath = videoPath.Replace(".mp4", "_processed.mp4");
                    
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
                    
                    string finalPath = await VideoPostProcessor.ProcessVideoAsync(processingOptions);
                    
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
                item.Status = "생성 완료";
                SaveQueueToFile();
                
                Console.WriteLine($"=== ✅ 영상 준비 완료: {item.FileName}");
                Console.WriteLine($"=== 저장된 경로: {videoPath}");
                Console.WriteLine($"=== 파일 존재 확인: {File.Exists(videoPath)}");
                
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
                var youtubeUploader = new YouTubeUploader();

                bool authSuccess = await youtubeUploader.AuthenticateAsync();
                if (!authSuccess)
                {
                    throw new Exception("YouTube 인증 실패");
                }

                var uploadInfo = new YouTubeUploader.VideoUploadInfo
                {
                    FilePath = item.FilePath,
                    Title = item.Title,
                    Description = item.Description,
                    Tags = item.Tags,
                    PrivacyStatus = item.PrivacySetting
                };

                var progress = new Progress<YouTubeUploader.UploadProgressInfo>(progressInfo =>
                {
                    if (progressInfo.Percentage % 25 == 0)
                    {
                        Console.WriteLine($"    업로드 진행률: {progressInfo.Percentage}% - {progressInfo.Status}");
                        _logger.LogInformation($"[업로드 진행] {item.FileName}: {progressInfo.Percentage}%");
                    }
                });

                string videoUrl = await youtubeUploader.UploadVideoAsync(uploadInfo, progress);

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
                        _logger.LogInformation($"[히스토리 업데이트] {item.FileName}");
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
                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        File.Delete(item.FilePath);
                        Console.WriteLine($"    🗑️ 임시 파일 삭제: {Path.GetFileName(item.FilePath)}");
                        _logger.LogInformation($"[임시 파일 삭제] {item.FilePath}");
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
