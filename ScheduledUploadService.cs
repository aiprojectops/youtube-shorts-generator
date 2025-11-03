using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Text.Json;

namespace YouTubeShortsWebApp
{
    public class ScheduledUploadService : BackgroundService
    {
        private readonly ILogger<ScheduledUploadService> _logger;
        private readonly SharedMemoryDataStore _dataStore;  // 🆕 이 줄 추가
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

        public ScheduledUploadService(
            ILogger<ScheduledUploadService> logger,
            SharedMemoryDataStore dataStore)  // 🆕 매개변수 추가
        {
            _logger = logger;
            _dataStore = dataStore;  // 🆕 이 줄 추가
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
            
            // 🔥 간단한 로그로 변경
            Console.WriteLine($"{item.FileName} {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
            
            _logger.LogInformation($"스케줄 추가: {item.FileName} at {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
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

        // ExecuteAsync 부분 로그 정리 (약 100줄)
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("🚀 스케줄 업로드 서비스 시작");
            
            // 자세한 로그들 제거
            // Console.WriteLine($"=== 현재 서버 시간: ...");
            // Console.WriteLine($"=== 대기 중인 업로드: ...");
        
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    List<ScheduledUploadItem> itemsToProcess;
        
                    lock (_queueLock)
                    {
                        itemsToProcess = _uploadQueue
                            .Where(x => x.Status == "대기 중" || x.Status == "생성 완료")
                            .OrderBy(x => x.ScheduledTime)
                            .ToList();
                    }
        
                    if (itemsToProcess.Count > 0)
                    {
                        foreach (var item in itemsToProcess)
                        {
                            try
                            {
                                // 영상 생성 5분 전
                                if (item.NeedsGeneration && 
                                    item.ScheduledTime.AddMinutes(-5) <= now && 
                                    string.IsNullOrEmpty(item.FilePath) &&
                                    item.Status == "대기 중")
                                {
                                    try
                                    {
                                        await GenerateVideoForUpload(item);
                                        _logger.LogInformation($"[영상 생성 완료] {item.FileName}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"❌ 영상 생성 실패: {item.FileName}");
                                        item.Status = "생성 실패";
                                        item.ErrorMessage = ex.Message;
                                        SaveQueueToFile();
                                        
                                        _currentBatchCompleted++;
                                        CheckBatchCompletion();
                                        continue;
                                    }
                                }
                                
                                // 업로드 시간
                                if (item.ScheduledTime <= now && 
                                    !string.IsNullOrEmpty(item.FilePath) && 
                                    File.Exists(item.FilePath) &&
                                    item.Status == "생성 완료")
                                {
                                    try
                                    {
                                        await ProcessUpload(item);
                                        
                                        _currentBatchCompleted++;
                                        _currentBatchSuccess++;
                                        CheckBatchCompletion();
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"❌ 업로드 실패: {item.FileName}");
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
                                Console.WriteLine($"❌ 처리 오류: {item.FileName}");
                                item.Status = "오류";
                                item.ErrorMessage = itemEx.Message;
                                SaveQueueToFile();
                                
                                _currentBatchCompleted++;
                                CheckBatchCompletion();
                            }
                        }
        
                        // 완료 항목 제거
                        lock (_queueLock)
                        {
                            _uploadQueue.RemoveAll(x => 
                                x.Status == "완료" || 
                                x.Status == "실패" || 
                                x.Status == "오류" ||
                                x.Status == "생성 실패");
                        }
                        SaveQueueToFile();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ 스케줄 서비스 오류: {ex.Message}");
                }
        
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        
            Console.WriteLine("🛑 스케줄 업로드 서비스 종료");
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
            Console.WriteLine($"📹 영상 생성 중: {item.FileName}");
            
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
                
                // 로그 간소화 - 이 부분 삭제
                // Console.WriteLine($"=== 영상 생성 요청 완료. Prediction ID: {prediction.id}");
                
                var progress = new Progress<ReplicateClient.ProgressInfo>(info =>
                {
                    // 진행률 로그 제거 또는 간소화
                    // Console.WriteLine($"    생성 진행률: {info.Percentage}% - {info.Status}");
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
                
                // 다운로드
                using var httpClient = new System.Net.Http.HttpClient();
                byte[] videoBytes = await httpClient.GetByteArrayAsync(videoUrl);
                
                string tempPath = Path.GetTempPath();
                string videoPath = Path.Combine(tempPath, item.FileName);
                await File.WriteAllBytesAsync(videoPath, videoBytes);
                
                // 후처리
                if (item.EnablePostProcessing && !string.IsNullOrEmpty(item.CaptionText))
                {
                    Console.WriteLine($"🎬 후처리 중: {item.FileName}");
                    
                    var processingOptions = new VideoPostProcessor.ProcessingOptions
                    {
                        InputVideoPath = videoPath,
                        OutputVideoPath = videoPath.Replace(".mp4", "_processed.mp4"),
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
                    }
                    catch (Exception ex)
                    {
                        videoPath = finalPath;
                    }
                }
                
                item.FilePath = videoPath;
                item.Status = "생성 완료";
                SaveQueueToFile();
                
                Console.WriteLine($"✅ 영상 생성 완료: {item.FileName}");
                
                // 히스토리 저장 (로그 제거)
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
                }
                catch (Exception historyEx)
                {
                    // 히스토리 저장 실패 로그 제거
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 영상 생성 실패: {item.FileName} - {ex.Message}");
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

       // 업로드 처리 부분 (약 450줄)
        private async Task ProcessUpload(ScheduledUploadItem item)
        {
            var startTime = DateTime.Now;
            
            Console.WriteLine($"📤 업로드 중: {item.FileName}");
        
            item.Status = "업로드 중";
            item.StartTime = startTime;
            SaveQueueToFile();
        
            try
            {
                // 🔥 저장된 userId와 refreshToken으로 인증
                var youtubeUploader = new YouTubeUploader(item.UserId, _dataStore);  // 🆕 _dataStore 추가
                bool authSuccess = await youtubeUploader.AuthenticateWithRefreshTokenAsync(item.RefreshToken);
                
                if (!authSuccess)
                {
                    throw new Exception("YouTube 인증 실패");
                }
        
                // VideoUploadInfo 객체 생성
                var uploadInfo = new YouTubeUploader.VideoUploadInfo
                {
                    FilePath = item.FilePath,
                    Title = item.Title,
                    Description = item.Description,
                    Tags = item.Tags,  // 문자열 그대로 전달 (Split은 UploadVideoAsync 안에서 처리됨)
                    PrivacyStatus = item.PrivacySetting
                };
                
                // 객체를 전달하여 업로드
                string uploadedUrl = await youtubeUploader.UploadVideoAsync(uploadInfo);
        
                item.Status = "완료";
                item.UploadedUrl = uploadedUrl;
                item.CompletedTime = DateTime.Now;
        
                Console.WriteLine($"✅ 업로드 완료: {item.FileName}");
                
                // 자세한 로그 제거
                // Console.WriteLine($"    URL: {uploadedUrl}");
                // Console.WriteLine($"    소요 시간: ...");
            }
            catch (Exception ex)
            {
                item.Status = "실패";
                item.ErrorMessage = ex.Message;
                item.CompletedTime = DateTime.Now;
        
                Console.WriteLine($"❌ 업로드 실패: {item.FileName} - {ex.Message}");
                throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        File.Delete(item.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    // 파일 삭제 실패 로그 제거
                }
                
                SaveQueueToFile();
            }
        }

        /// <summary>
        /// 모든 스케줄 강제 취소
        /// </summary>
        public int ClearAllSchedules()
        {
            int clearedCount = 0;
            
            lock (_queueLock)
            {
                clearedCount = _uploadQueue.Count;
                _uploadQueue.Clear();
                
                // 배치 카운터 리셋
                _batchStartTime = DateTime.MinValue;
                _currentBatchTotal = 0;
                _currentBatchCompleted = 0;
                _currentBatchSuccess = 0;
            }
            
            SaveQueueToFile();
            
            Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"🛑 [강제 종료] 모든 스케줄 취소됨: {clearedCount}개");
            Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            
            _logger.LogInformation($"모든 스케줄 강제 취소: {clearedCount}개");
            
            return clearedCount;
        }

    }


    public class ScheduledUploadItem
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime ScheduledTime { get; set; }

        // 🔥 이 2줄 추가
        public string UserId { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        
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
