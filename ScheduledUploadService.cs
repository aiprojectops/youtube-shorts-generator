using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Text.Json;

namespace YouTubeShortsWebApp
{
    public class ScheduledUploadService : BackgroundService
    {
        private readonly ILogger<ScheduledUploadService> _logger;
        private readonly SharedMemoryDataStore _dataStore;
        private static readonly string QueueDirectory = Path.Combine(
            Path.GetTempPath(), 
            "YouTubeScheduledQueues"  // 🔥 폴더로 변경
        );

        // 🔥 유저별 큐로 변경
        private readonly ConcurrentDictionary<string, List<ScheduledUploadItem>> _userQueues = new();
        private readonly object _queueLock = new object();

        // 🔥 유저별 배치 추적
        private readonly ConcurrentDictionary<string, BatchInfo> _userBatches = new();

        // 🔥 유저별 완료 이벤트
        public event Action<string, int, int, List<ScheduledUploadItem>>? OnAllUploadsCompleted;

        public ScheduledUploadService(
            ILogger<ScheduledUploadService> logger,
            SharedMemoryDataStore dataStore)
        {
            _logger = logger;
            _dataStore = dataStore;
            
            // 디렉토리 생성
            if (!Directory.Exists(QueueDirectory))
            {
                Directory.CreateDirectory(QueueDirectory);
            }
            
            LoadAllQueuesFromFiles();
        }

        // 🔥 모든 유저의 큐 로드
        private void LoadAllQueuesFromFiles()
        {
            try
            {
                if (!Directory.Exists(QueueDirectory))
                    return;

                var queueFiles = Directory.GetFiles(QueueDirectory, "*.json");
                
                foreach (var filePath in queueFiles)
                {
                    try
                    {
                        string userId = Path.GetFileNameWithoutExtension(filePath);
                        string json = File.ReadAllText(filePath);
                        var items = JsonSerializer.Deserialize<List<ScheduledUploadItem>>(json);
                        
                        if (items != null && items.Count > 0)
                        {
                            var pendingItems = items.Where(x => 
                                x.Status == "대기 중" || x.Status == "생성 완료").ToList();
                            
                            if (pendingItems.Count > 0)
                            {
                                _userQueues[userId] = pendingItems;
                                Console.WriteLine($"[{userId}] 스케줄 복구: {pendingItems.Count}개");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"큐 파일 로드 실패 ({filePath}): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"큐 디렉토리 로드 실패: {ex.Message}");
            }
        }

        // 🔥 유저별 큐 파일 저장
        private void SaveQueueToFile(string userId)
        {
            try
            {
                if (!_userQueues.ContainsKey(userId))
                    return;

                string filePath = Path.Combine(QueueDirectory, $"{userId}.json");
                string json = JsonSerializer.Serialize(_userQueues[userId], new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{userId}] 큐 저장 실패: {ex.Message}");
            }
        }

        // 🔥 userId 파라미터 추가
        public void AddScheduledUpload(string userId, ScheduledUploadItem item)
        {
            lock (_queueLock)
            {
                if (!_userQueues.ContainsKey(userId))
                {
                    _userQueues[userId] = new List<ScheduledUploadItem>();
                }
                
                _userQueues[userId].Add(item);
                
                // 배치 추적 시작
                if (!_userBatches.ContainsKey(userId))
                {
                    _userBatches[userId] = new BatchInfo
                    {
                        StartTime = DateTime.Now,
                        Total = 0,
                        Completed = 0,
                        Success = 0
                    };
                }
                _userBatches[userId].Total++;
            }
            
            SaveQueueToFile(userId);
            
            Console.WriteLine($"[{userId}] {item.FileName} {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}");
        }

        // 🔥 userId 파라미터 추가
        public List<ScheduledUploadItem> GetAllScheduledItems(string userId)
        {
            lock (_queueLock)
            {
                return _userQueues.ContainsKey(userId) 
                    ? _userQueues[userId].ToList() 
                    : new List<ScheduledUploadItem>();
            }
        }

        // 🔥 userId 파라미터 추가
        public int GetQueueCount(string userId)
        {
            lock (_queueLock)
            {
                if (!_userQueues.ContainsKey(userId))
                    return 0;

                return _userQueues[userId].Count(x => 
                    x.Status == "대기 중" || x.Status == "생성 완료");
            }
        }

        // 🔥 모든 유저 처리
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("🚀 스케줄 업로드 서비스 시작");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var userIds = _userQueues.Keys.ToList();

                    // 🔥 각 유저별로 처리
                    foreach (var userId in userIds)
                    {
                        try
                        {
                            await ProcessUserQueue(userId, now, stoppingToken);
                        }
                        catch (Exception userEx)
                        {
                            _logger.LogError($"[{userId}] 처리 오류: {userEx.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"스케줄 서비스 오류: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            Console.WriteLine("🛑 스케줄 업로드 서비스 종료");
        }

        // 🔥 유저별 큐 처리
        private async Task ProcessUserQueue(string userId, DateTime now, CancellationToken stoppingToken)
        {
            List<ScheduledUploadItem> itemsToProcess;

            lock (_queueLock)
            {
                if (!_userQueues.ContainsKey(userId))
                    return;

                itemsToProcess = _userQueues[userId]
                    .Where(x => x.Status == "대기 중" || x.Status == "생성 완료")
                    .OrderBy(x => x.ScheduledTime)
                    .ToList();
            }

            if (itemsToProcess.Count == 0)
                return;

            foreach (var item in itemsToProcess)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    // 영상 생성
                    if (item.NeedsGeneration && 
                        item.ScheduledTime.AddMinutes(-5) <= now && 
                        string.IsNullOrEmpty(item.FilePath) &&
                        item.Status == "대기 중")
                    {
                        try
                        {
                            await GenerateVideoForUpload(userId, item);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[{userId}] ❌ 영상 생성 실패: {item.FileName}");
                            item.Status = "생성 실패";
                            item.ErrorMessage = ex.Message;
                            SaveQueueToFile(userId);
                            
                            UpdateBatchProgress(userId, false);
                            continue;
                        }
                    }
                    
                    // 업로드
                    if (item.ScheduledTime <= now && 
                        !string.IsNullOrEmpty(item.FilePath) && 
                        File.Exists(item.FilePath) &&
                        item.Status == "생성 완료")
                    {
                        try
                        {
                            await ProcessUpload(userId, item);
                            UpdateBatchProgress(userId, true);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[{userId}] ❌ 업로드 실패: {item.FileName}");
                            item.Status = "실패";
                            item.ErrorMessage = ex.Message;
                            item.CompletedTime = DateTime.Now;
                            SaveQueueToFile(userId);
                            
                            UpdateBatchProgress(userId, false);
                        }
                    }
                }
                catch (Exception itemEx)
                {
                    Console.WriteLine($"[{userId}] ❌ 처리 오류: {item.FileName}");
                    item.Status = "오류";
                    item.ErrorMessage = itemEx.Message;
                    SaveQueueToFile(userId);
                    
                    UpdateBatchProgress(userId, false);
                }
            }

            // 완료 항목 제거
            lock (_queueLock)
            {
                if (_userQueues.ContainsKey(userId))
                {
                    _userQueues[userId].RemoveAll(x => 
                        x.Status == "완료" || 
                        x.Status == "실패" || 
                        x.Status == "오류" ||
                        x.Status == "생성 실패");
                }
            }
            SaveQueueToFile(userId);
        }

        // 🔥 배치 진행률 업데이트
        private void UpdateBatchProgress(string userId, bool success)
        {
            if (!_userBatches.ContainsKey(userId))
                return;

            var batch = _userBatches[userId];
            batch.Completed++;
            if (success) batch.Success++;

            CheckBatchCompletion(userId);
        }

        // 🔥 유저별 배치 완료 확인
        private void CheckBatchCompletion(string userId)
        {
            if (!_userBatches.ContainsKey(userId))
                return;

            var batch = _userBatches[userId];
            
            if (batch.Total > 0 && batch.Completed >= batch.Total)
            {
                var duration = DateTime.Now - batch.StartTime;
                
                Console.WriteLine("");
                Console.WriteLine($"=== [{userId}] 배치 완료 ===");
                Console.WriteLine($"성공: {batch.Success}/{batch.Total}");
                Console.WriteLine($"소요: {duration.TotalMinutes:F1}분");
                Console.WriteLine("=============================");

                // 완료된 항목 가져오기
                List<ScheduledUploadItem> completedItems;
                lock (_queueLock)
                {
                    if (_userQueues.ContainsKey(userId))
                    {
                        completedItems = _userQueues[userId]
                            .Where(x => x.Status == "완료" || x.Status == "실패" || 
                                       x.Status == "오류" || x.Status == "생성 실패")
                            .ToList();
                    }
                    else
                    {
                        completedItems = new List<ScheduledUploadItem>();
                    }
                }

                // 이벤트 발생
                OnAllUploadsCompleted?.Invoke(userId, batch.Success, batch.Total, completedItems);

                // 배치 정보 제거
                _userBatches.TryRemove(userId, out _);
            }
        }

        private async Task GenerateVideoForUpload(string userId, ScheduledUploadItem item)
        {
            Console.WriteLine($"[{userId}] 📹 영상 생성 중: {item.FileName}");
            
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
                
                var progress = new Progress<ReplicateClient.ProgressInfo>(info => { });
                
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
                
                using var httpClient = new System.Net.Http.HttpClient();
                byte[] videoBytes = await httpClient.GetByteArrayAsync(videoUrl);
                
                string tempPath = Path.GetTempPath();
                string videoPath = Path.Combine(tempPath, $"{userId}_{item.FileName}");
                await File.WriteAllBytesAsync(videoPath, videoBytes);
                
                if (item.EnablePostProcessing && !string.IsNullOrEmpty(item.CaptionText))
                {
                    Console.WriteLine($"[{userId}] 🎬 후처리 중: {item.FileName}");
                    
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
                    catch
                    {
                        videoPath = finalPath;
                    }
                }
                
                item.FilePath = videoPath;
                item.Status = "생성 완료";
                SaveQueueToFile(userId);
                
                Console.WriteLine($"[{userId}] ✅ 영상 생성 완료: {item.FileName}");
                
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
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{userId}] ❌ 영상 생성 실패: {item.FileName} - {ex.Message}");
                item.Status = "생성 실패";
                item.ErrorMessage = ex.Message;
                SaveQueueToFile(userId);
                throw;
            }
            finally
            {
                replicateClient.Dispose();
            }
        }

        private async Task ProcessUpload(string userId, ScheduledUploadItem item)
        {
            var startTime = DateTime.Now;
            
            Console.WriteLine($"[{userId}] 📤 업로드 중: {item.FileName}");
        
            item.Status = "업로드 중";
            item.StartTime = startTime;
            SaveQueueToFile(userId);
        
            try
            {
                var youtubeUploader = new YouTubeUploader(item.UserId, _dataStore);
                bool authSuccess = await youtubeUploader.AuthenticateWithRefreshTokenAsync(item.RefreshToken);
                
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
                
                string uploadedUrl = await youtubeUploader.UploadVideoAsync(uploadInfo);
        
                item.Status = "완료";
                item.UploadedUrl = uploadedUrl;
                item.CompletedTime = DateTime.Now;
        
                Console.WriteLine($"[{userId}] ✅ 업로드 완료: {item.FileName}");
            }
            catch (Exception ex)
            {
                item.Status = "실패";
                item.ErrorMessage = ex.Message;
                item.CompletedTime = DateTime.Now;
        
                Console.WriteLine($"[{userId}] ❌ 업로드 실패: {item.FileName} - {ex.Message}");
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
                catch { }
                
                SaveQueueToFile(userId);
            }
        }

        // 🔥 userId 파라미터 추가
        public int ClearAllSchedules(string userId)
        {
            int clearedCount = 0;
            
            lock (_queueLock)
            {
                if (_userQueues.ContainsKey(userId))
                {
                    clearedCount = _userQueues[userId].Count;
                    _userQueues[userId].Clear();
                    _userQueues.TryRemove(userId, out _);
                }
                
                if (_userBatches.ContainsKey(userId))
                {
                    _userBatches.TryRemove(userId, out _);
                }
            }
            
            SaveQueueToFile(userId);
            
            Console.WriteLine($"[{userId}] 🛑 모든 스케줄 취소: {clearedCount}개");
            
            return clearedCount;
        }
    }

    // 🔥 배치 정보 클래스 추가
    public class BatchInfo
    {
        public DateTime StartTime { get; set; }
        public int Total { get; set; }
        public int Completed { get; set; }
        public int Success { get; set; }
    }

    public class ScheduledUploadItem
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime ScheduledTime { get; set; }
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
