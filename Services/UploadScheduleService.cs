using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace YouTubeShortsWebApp.Services
{
    /// <summary>
    /// YouTube 업로드 + 스케줄 통합 서비스
    /// YouTubeUpload, AllInOne에서 공통으로 사용
    /// </summary>
    public class UploadScheduleService
    {
        private readonly YouTubeUploadService _uploadService;
        private readonly ScheduledUploadService _scheduledService;

        public UploadScheduleService(
            YouTubeUploadService uploadService,
            ScheduledUploadService scheduledService)
        {
            _uploadService = uploadService;
            _scheduledService = scheduledService;
        }

        /// <summary>
        /// 스케줄 설정
        /// </summary>
        public class ScheduleSettings
        {
            public float Hours { get; set; } = 2.0f;
            public int MinIntervalMinutes { get; set; } = 7;
            public bool RandomizeOrder { get; set; } = true;
            public Dictionary<int, DateTime> ScheduledTimes { get; set; } = new();
        }

        /// <summary>
        /// 업로드 요청
        /// </summary>
        public class UploadRequest
        {
            public List<string> FilePaths { get; set; } = new();
            public YouTubeUploadService.UploadOptions UploadOptions { get; set; }
            public bool IsScheduledUpload { get; set; } = false;
            public ScheduleSettings Schedule { get; set; }
        }

        /// <summary>
        /// 업로드 결과
        /// </summary>
        public class UploadResult
        {
            public bool Success { get; set; }
            public string VideoUrl { get; set; }
            public string FileName { get; set; }
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// 진행 상황 콜백
        /// (현재 번호, 전체 개수, 파일명)
        /// </summary>
        public delegate void ProgressCallback(int current, int total, string fileName);

        /// <summary>
        /// 즉시 업로드 실행
        /// </summary>
        public async Task<List<UploadResult>> UploadImmediatelyAsync(
            UploadRequest request,
            ProgressCallback progressCallback = null)
        {
            var results = new List<UploadResult>();
        
            Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"📤 즉시 업로드 시작: 총 {request.FilePaths.Count}개");
            Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        
            for (int i = 0; i < request.FilePaths.Count; i++)
            {
                string filePath = request.FilePaths[i];
                string fileName = System.IO.Path.GetFileName(filePath);
                int currentIndex = i + 1;
        
                try
                {
                    progressCallback?.Invoke(currentIndex, request.FilePaths.Count, fileName);
        
                    // 🆕 제목/설명/태그 선택 로직 추가
                    string title, description, tags;
        
                    if (request.UploadOptions.UseRandomInfo)
                    {
                        // 랜덤 정보 사용
                        var titleRandom = new Random(Guid.NewGuid().GetHashCode());
                        var descRandom = new Random(Guid.NewGuid().GetHashCode());
                        var tagsRandom = new Random(Guid.NewGuid().GetHashCode());
        
                        title = request.UploadOptions.RandomTitles != null && request.UploadOptions.RandomTitles.Count > 0
                            ? request.UploadOptions.RandomTitles[titleRandom.Next(request.UploadOptions.RandomTitles.Count)]
                            : (request.FilePaths.Count > 1
                                ? request.UploadOptions.TitleTemplate.Replace("#NUMBER", $"#{i + 1}")
                                : request.UploadOptions.TitleTemplate.Replace(" #NUMBER", ""));
        
                        description = request.UploadOptions.RandomDescriptions != null && request.UploadOptions.RandomDescriptions.Count > 0
                            ? request.UploadOptions.RandomDescriptions[descRandom.Next(request.UploadOptions.RandomDescriptions.Count)]
                            : request.UploadOptions.Description;
        
                        tags = request.UploadOptions.RandomTags != null && request.UploadOptions.RandomTags.Count > 0
                            ? request.UploadOptions.RandomTags[tagsRandom.Next(request.UploadOptions.RandomTags.Count)]
                            : request.UploadOptions.Tags;
                    }
                    else
                    {
                        // 일반 모드
                        title = request.FilePaths.Count > 1
                            ? request.UploadOptions.TitleTemplate.Replace("#NUMBER", $"#{i + 1}")
                            : request.UploadOptions.TitleTemplate.Replace(" #NUMBER", "");
                        description = request.UploadOptions.Description;
                        tags = request.UploadOptions.Tags;
                    }
        
                    // 🆕 UploadOptions 복사본 생성 (개별 업로드용)
                    var individualOptions = new YouTubeUploadService.UploadOptions
                    {
                        TitleTemplate = title,
                        Description = description,
                        Tags = tags,
                        PrivacySetting = request.UploadOptions.PrivacySetting,
                        UseRandomInfo = false  // 이미 선택했으므로 false
                    };
        
                    string videoUrl = await _uploadService.UploadSingleVideoAsync(
                        filePath,
                        title,  // 🆕 제목 사용
                        individualOptions,
                        null // 진행률 콜백은 선택사항
                    );
        
                    results.Add(new UploadResult
                    {
                        Success = true,
                        VideoUrl = videoUrl,
                        FileName = fileName
                    });
        
                    Console.WriteLine($"✅ 업로드 완료 [{currentIndex}]: {videoUrl}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 업로드 실패 [{currentIndex}]: {ex.Message}");
        
                    results.Add(new UploadResult
                    {
                        Success = false,
                        FileName = fileName,
                        ErrorMessage = ex.Message
                    });
                }
            }
        
            int successCount = results.Count(r => r.Success);
            Console.WriteLine($"");
            Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"📤 즉시 업로드 완료: {successCount}/{request.FilePaths.Count} 성공");
            Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        
            return results;
        }
        
        /// <summary>
        /// 스케줄 업로드 등록
        /// </summary>
        public void RegisterScheduledUpload(UploadRequest request)
        {
            Console.WriteLine($"⏰ 스케줄 업로드 등록: 총 {request.FilePaths.Count}개");

            _uploadService.RegisterScheduledUploads(
                request.FilePaths,
                request.UploadOptions,
                request.Schedule.ScheduledTimes,
                request.Schedule.RandomizeOrder,
                _scheduledService
            );

            Console.WriteLine($"✅ 스케줄 등록 완료");
        }

        /// <summary>
        /// AI 생성 정보로 스케줄 업로드 등록
        /// (생성은 업로드 5분 전에 자동 실행)
        /// </summary>
        public void RegisterScheduledUploadWithGeneration(
            List<YouTubeUploadService.VideoGenerationInfo> videoInfoList,
            YouTubeUploadService.UploadOptions uploadOptions,
            ScheduleSettings schedule)
        {
            Console.WriteLine($"⏰ AI 생성 + 스케줄 업로드 등록: 총 {videoInfoList.Count}개");

            _uploadService.RegisterScheduledUploadsWithGeneration(
                videoInfoList,
                uploadOptions,
                schedule.ScheduledTimes,
                schedule.RandomizeOrder,
                _scheduledService
            );

            Console.WriteLine($"✅ 생성 스케줄 등록 완료 (업로드 5분 전에 자동 생성)");
        }

        /// <summary>
        /// 스케줄 미리보기 생성
        /// </summary>
        public List<SchedulePreviewItem> GenerateSchedulePreview(
            int videoCount,
            ScheduleSettings settings)
        {
            var preview = new List<SchedulePreviewItem>();
            DateTime startTime = DateTime.Now.AddMinutes(5);

            // 총 분산 시간을 분 단위로 변환
            int totalMinutes = (int)(settings.Hours * 60);

            // 영상 개수에 맞게 간격 계산
            int intervalMinutes = videoCount > 1
                ? totalMinutes / (videoCount - 1)
                : 0;

            // 최소 간격 보장
            if (intervalMinutes < settings.MinIntervalMinutes)
            {
                intervalMinutes = settings.MinIntervalMinutes;
            }

            for (int i = 0; i < videoCount; i++)
            {
                DateTime scheduledTime = startTime.AddMinutes(i * intervalMinutes);

                preview.Add(new SchedulePreviewItem
                {
                    Index = i + 1,
                    ScheduledTime = scheduledTime
                });
            }

            // 순서 랜덤화
            if (settings.RandomizeOrder && videoCount > 1)
            {
                var random = new Random();
                var times = preview.Select(p => p.ScheduledTime).OrderBy(x => random.Next()).ToList();
                for (int i = 0; i < preview.Count; i++)
                {
                    preview[i].ScheduledTime = times[i];
                }
            }

            return preview.OrderBy(p => p.ScheduledTime).ToList();
        }

        /// <summary>
        /// 스케줄 미리보기용 DTO로 변환
        /// </summary>
        public Dictionary<int, DateTime> ConvertPreviewToSchedule(List<SchedulePreviewItem> preview)
        {
            return preview.ToDictionary(p => p.Index - 1, p => p.ScheduledTime);
        }

        /// <summary>
        /// 스케줄 미리보기 항목
        /// </summary>
        public class SchedulePreviewItem
        {
            public int Index { get; set; }
            public DateTime ScheduledTime { get; set; }
        }

        /// <summary>
        /// 현재 활성 스케줄 개수 조회
        /// </summary>
        public int GetActiveScheduleCount(string userId)
        {
            return _scheduledService.GetQueueCount(userId);
        }

        /// <summary>
        /// 다음 예정 업로드 정보
        /// </summary>
        public string GetNextUploadInfo(string userId)  // 🔥 userId 파라미터 추가
        {
            int count = GetActiveScheduleCount(userId);  // 🔥 userId 전달
            if (count == 0)
                return "";

            return $"대기 중인 업로드: {count}개";
        }

        /// <summary>
        /// 업로드 가능 여부 검증
        /// </summary>
        public (bool IsValid, string ErrorMessage) ValidateUploadRequest(UploadRequest request)
        {
            if (request == null)
                return (false, "요청이 null입니다.");

            if (request.FilePaths == null || request.FilePaths.Count == 0)
                return (false, "업로드할 파일이 없습니다.");

            if (!_uploadService.IsAuthenticated)
                return (false, "YouTube 인증이 필요합니다.");

            if (request.UploadOptions == null)
                return (false, "업로드 옵션이 설정되지 않았습니다.");

            // 랜덤 정보 사용 시 검증
            if (request.UploadOptions.UseRandomInfo)
            {
                if (request.UploadOptions.RandomTitles == null || request.UploadOptions.RandomTitles.Count == 0)
                    return (false, "랜덤 제목이 설정되지 않았습니다.");

                if (request.UploadOptions.RandomDescriptions == null || request.UploadOptions.RandomDescriptions.Count == 0)
                    return (false, "랜덤 설명이 설정되지 않았습니다.");

                if (request.UploadOptions.RandomTags == null || request.UploadOptions.RandomTags.Count == 0)
                    return (false, "랜덤 태그가 설정되지 않았습니다.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.UploadOptions.TitleTemplate))
                    return (false, "제목이 입력되지 않았습니다.");
            }

            // 스케줄 업로드 검증
            if (request.IsScheduledUpload)
            {
                if (request.Schedule == null)
                    return (false, "스케줄 설정이 null입니다.");

                if (request.Schedule.ScheduledTimes == null || request.Schedule.ScheduledTimes.Count == 0)
                    return (false, "스케줄 시간이 설정되지 않았습니다.");
            }

            return (true, "");
        }
    }
}
