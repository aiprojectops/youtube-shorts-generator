using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace YouTubeShortsWebApp
{
    public class YouTubeUploader
    {
        private static readonly string[] Scopes = {
            YouTubeService.Scope.YoutubeUpload,
            YouTubeService.Scope.YoutubeReadonly
        };
        private static readonly string ApplicationName = "YouTube Shorts Generator";

        private YouTubeService youtubeService;
        private UserCredential credential;

        // 현재 연동된 계정 정보
        public class YouTubeAccountInfo
        {
            public string ChannelTitle { get; set; }
            public string ChannelId { get; set; }
            public string Email { get; set; }
            public string ThumbnailUrl { get; set; }
            public string ChannelUrl { get; set; }
            public ulong SubscriberCount { get; set; }
            public ulong VideoCount { get; set; }
        }

        // 업로드 진행률 정보 클래스
        public class UploadProgressInfo
        {
            public long BytesSent { get; set; }
            public long TotalBytes { get; set; }
            public int Percentage { get; set; }
            public string Status { get; set; }
            public TimeSpan ElapsedTime { get; set; }
            public string VideoId { get; set; } // 추가: 업로드된 비디오 ID
        }

        // YouTube 업로드를 위한 비디오 정보 클래스
        public class VideoUploadInfo
        {
            public string FilePath { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string Tags { get; set; }
            public string PrivacyStatus { get; set; }
        }

        // OAuth 인증
        public async Task<bool> AuthenticateAsync(bool forceReauth = false)
        {
            try
            {
                string credPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "YouTubeShortsWebApp",
                    "youtube-credentials"
                );

                if (forceReauth && Directory.Exists(credPath))
                {
                    try
                    {
                        Directory.Delete(credPath, true);
                        System.Diagnostics.Debug.WriteLine("기존 인증 토큰 삭제됨");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"토큰 삭제 실패: {ex.Message}");
                    }
                }

                var config = ConfigManager.GetConfig();

                if (string.IsNullOrEmpty(config.YouTubeClientId) || string.IsNullOrEmpty(config.YouTubeClientSecret))
                {
                    throw new Exception("YouTube API 클라이언트 ID와 시크릿이 설정되지 않았습니다.\n설정 화면에서 먼저 등록해주세요.");
                }

                var clientSecrets = new ClientSecrets
                {
                    ClientId = config.YouTubeClientId,
                    ClientSecret = config.YouTubeClientSecret
                };

                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    clientSecrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)
                );

                youtubeService = new YouTubeService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YouTube 인증 실패: {ex.Message}");
                throw new Exception($"YouTube 인증 실패: {ex.Message}");
            }
        }

        // 현재 연동된 계정 정보 가져오기
        public async Task<YouTubeAccountInfo> GetCurrentAccountInfoAsync()
        {
            if (!IsAuthenticated())
            {
                throw new Exception("YouTube에 인증되지 않았습니다.");
            }

            try
            {
                var channelsRequest = youtubeService.Channels.List("snippet,statistics");
                channelsRequest.Mine = true;

                var channelsResponse = await channelsRequest.ExecuteAsync();

                if (channelsResponse.Items == null || channelsResponse.Items.Count == 0)
                {
                    throw new Exception("연동된 YouTube 채널을 찾을 수 없습니다.");
                }

                var channel = channelsResponse.Items[0];

                var accountInfo = new YouTubeAccountInfo
                {
                    ChannelTitle = channel.Snippet.Title,
                    ChannelId = channel.Id,
                    ThumbnailUrl = channel.Snippet.Thumbnails?.Default__?.Url,
                    ChannelUrl = $"https://www.youtube.com/channel/{channel.Id}",
                    SubscriberCount = channel.Statistics?.SubscriberCount ?? 0,
                    VideoCount = channel.Statistics?.VideoCount ?? 0
                };

                try
                {
                    accountInfo.Email = "YouTube 계정";
                }
                catch
                {
                    accountInfo.Email = "YouTube 계정";
                }

                return accountInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"계정 정보 가져오기 오류: {ex.Message}");
                throw new Exception($"계정 정보를 가져오는 중 오류 발생: {ex.Message}");
            }
        }

        // 인증 상태 확인
        public bool IsAuthenticated()
        {
            return youtubeService != null && credential != null;
        }

        // 계정 변경을 위한 재인증
        public async Task<bool> SwitchAccountAsync()
        {
            await RevokeAuthenticationAsync();
            return await AuthenticateAsync(forceReauth: true);
        }

        // 인증 해제
        public async Task RevokeAuthenticationAsync()
        {
            try
            {
                if (credential != null)
                {
                    await credential.RevokeTokenAsync(CancellationToken.None);
                }

                string credPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "YouTubeShortsWebApp",
                    "youtube-credentials"
                );

                if (Directory.Exists(credPath))
                {
                    Directory.Delete(credPath, true);
                }

                youtubeService?.Dispose();
                youtubeService = null;
                credential = null;

                System.Diagnostics.Debug.WriteLine("YouTube 인증이 해제되었습니다.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"인증 해제 실패: {ex.Message}");
            }
        }

        // 비디오 파일 검증 메서드 추가
        private bool ValidateVideoFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }

                var fileInfo = new FileInfo(filePath);

                // 파일 크기 검사 (YouTube 제한: 256GB, 하지만 실용적으로 2GB로 제한)
                const long maxSize = 2L * 1024 * 1024 * 1024; // 2GB
                if (fileInfo.Length > maxSize)
                {
                    System.Diagnostics.Debug.WriteLine($"파일이 너무 큼: {fileInfo.Length / 1024 / 1024}MB");
                    return false;
                }

                // 파일 확장자 검사
                string extension = fileInfo.Extension.ToLower();
                var allowedExtensions = new[] { ".mp4", ".mov", ".avi", ".wmv", ".flv", ".webm", ".mkv" };

                return Array.Exists(allowedExtensions, ext => ext == extension);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"파일 검증 오류: {ex.Message}");
                return false;
            }
        }

        // 개선된 비디오 업로드 메서드
        public async Task<string> UploadVideoAsync(VideoUploadInfo uploadInfo, IProgress<UploadProgressInfo> progress = null, CancellationToken cancellationToken = default)
        {
            if (!IsAuthenticated())
            {
                throw new Exception("YouTube에 인증되지 않았습니다. 먼저 인증을 완료해주세요.");
            }

            if (!ValidateVideoFile(uploadInfo.FilePath))
            {
                throw new Exception($"비디오 파일이 유효하지 않거나 지원되지 않는 형식입니다: {uploadInfo.FilePath}");
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"업로드 시작: {Path.GetFileName(uploadInfo.FilePath)}");

                var videoMetadata = new Video();
                videoMetadata.Snippet = new VideoSnippet();
                videoMetadata.Snippet.Title = uploadInfo.Title;
                videoMetadata.Snippet.Description = uploadInfo.Description;
                videoMetadata.Snippet.CategoryId = "22"; // People & Blogs 카테고리

                // 태그 설정
                if (!string.IsNullOrEmpty(uploadInfo.Tags))
                {
                    var tagList = uploadInfo.Tags.Split(',');
                    videoMetadata.Snippet.Tags = new List<string>();
                    foreach (var tag in tagList)
                    {
                        var trimmedTag = tag.Trim();
                        if (!string.IsNullOrEmpty(trimmedTag) && trimmedTag.Length <= 500) // YouTube 태그 길이 제한
                        {
                            videoMetadata.Snippet.Tags.Add(trimmedTag);
                        }
                    }
                }

                // 공개 설정
                videoMetadata.Status = new VideoStatus();
                switch (uploadInfo.PrivacyStatus?.ToLower())
                {
                    case "공개":
                        videoMetadata.Status.PrivacyStatus = "public";
                        break;
                    case "링크 공유":
                        videoMetadata.Status.PrivacyStatus = "unlisted";
                        break;
                    case "목록에 없음":
                        videoMetadata.Status.PrivacyStatus = "unlisted";
                        break;
                    default:
                        videoMetadata.Status.PrivacyStatus = "private";
                        break;
                }

                // 쇼츠 감지 및 설정
                videoMetadata.Status.SelfDeclaredMadeForKids = false; // 중요: 쇼츠의 경우 필수

                string uploadedVideoId = null;

                using (var fileStream = new FileStream(uploadInfo.FilePath, FileMode.Open, FileAccess.Read))
                {
                    var videosInsertRequest = youtubeService.Videos.Insert(videoMetadata, "snippet,status", fileStream, "video/*");

                    // 청크 크기 설정 (안정성 향상)
                    videosInsertRequest.ChunkSize = ResumableUpload.MinimumChunkSize * 4; // 1MB

                    DateTime startTime = DateTime.Now;
                    long totalBytes = fileStream.Length;

                    // 응답 수신 이벤트
                    videosInsertRequest.ResponseReceived += (uploadedVideo) =>
                    {
                        uploadedVideoId = uploadedVideo.Id;
                        System.Diagnostics.Debug.WriteLine($"업로드 완료: 비디오 ID = {uploadedVideoId}");
                    };

                    // 진행률 추적을 위한 타이머 (실제 진행률 추적이 어려우므로)
                    var progressTimer = new System.Timers.Timer(1000); // 1초마다
                    int simulatedProgress = 0;
                    bool uploadCompleted = false;

                    progressTimer.Elapsed += (sender, e) =>
                    {
                        if (!uploadCompleted && simulatedProgress < 90)
                        {
                            simulatedProgress += 2;
                            var elapsed = DateTime.Now - startTime;

                            progress?.Report(new UploadProgressInfo
                            {
                                BytesSent = (long)(totalBytes * (simulatedProgress / 100.0)),
                                TotalBytes = totalBytes,
                                Percentage = simulatedProgress,
                                Status = "업로드 중",
                                ElapsedTime = elapsed
                            });
                        }
                    };

                    progressTimer.Start();

                    try
                    {
                        // 업로드 실행
                        var uploadResult = await videosInsertRequest.UploadAsync(cancellationToken);

                        progressTimer.Stop();
                        uploadCompleted = true;

                        if (uploadResult.Status == UploadStatus.Failed)
                        {
                            string errorMessage = uploadResult.Exception?.Message ?? "알 수 없는 오류";
                            System.Diagnostics.Debug.WriteLine($"업로드 실패: {errorMessage}");
                            throw new Exception($"업로드 실패: {errorMessage}");
                        }

                        if (uploadResult.Status != UploadStatus.Completed)
                        {
                            throw new Exception($"업로드가 완료되지 않음: {uploadResult.Status}");
                        }

                        if (string.IsNullOrEmpty(uploadedVideoId))
                        {
                            throw new Exception("업로드는 완료되었지만 비디오 ID를 받지 못했습니다.");
                        }

                        // 업로드 완료 후 처리 상태 확인
                        progress?.Report(new UploadProgressInfo
                        {
                            BytesSent = totalBytes,
                            TotalBytes = totalBytes,
                            Percentage = 95,
                            Status = "YouTube 처리 중",
                            ElapsedTime = DateTime.Now - startTime,
                            VideoId = uploadedVideoId
                        });

                        // YouTube 처리 상태 확인 (최대 2분 대기)
                        bool processingComplete = await WaitForVideoProcessing(uploadedVideoId, progress, cancellationToken);

                        if (!processingComplete)
                        {
                            System.Diagnostics.Debug.WriteLine($"경고: 비디오 처리가 완료되지 않았지만 계속 진행합니다. ID: {uploadedVideoId}");
                        }

                        // 최종 완료 상태
                        progress?.Report(new UploadProgressInfo
                        {
                            BytesSent = totalBytes,
                            TotalBytes = totalBytes,
                            Percentage = 100,
                            Status = processingComplete ? "업로드 및 처리 완료" : "업로드 완료 (처리 진행 중)",
                            ElapsedTime = DateTime.Now - startTime,
                            VideoId = uploadedVideoId
                        });

                        string videoUrl = $"https://www.youtube.com/watch?v={uploadedVideoId}";
                        System.Diagnostics.Debug.WriteLine($"최종 URL: {videoUrl}");

                        return videoUrl;
                    }
                    catch (Exception ex)
                    {
                        progressTimer?.Stop();
                        System.Diagnostics.Debug.WriteLine($"업로드 실행 오류: {ex.Message}");
                        throw;
                    }
                    finally
                    {
                        progressTimer?.Stop();
                        progressTimer?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"비디오 업로드 전체 오류: {ex.Message}");
                throw new Exception($"비디오 업로드 중 오류 발생: {ex.Message}");
            }
        }

        // YouTube 비디오 처리 상태 확인 메서드 추가
        private async Task<bool> WaitForVideoProcessing(string videoId, IProgress<UploadProgressInfo> progress, CancellationToken cancellationToken)
        {
            const int maxAttempts = 24; // 2분 대기 (5초 간격)
            const int delaySeconds = 5;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var videoRequest = youtubeService.Videos.List("status,processingDetails");
                    videoRequest.Id = videoId;

                    var videoResponse = await videoRequest.ExecuteAsync();

                    if (videoResponse.Items != null && videoResponse.Items.Count > 0)
                    {
                        var video = videoResponse.Items[0];
                        var uploadStatus = video.Status?.UploadStatus;
                        var processingStatus = video.ProcessingDetails?.ProcessingStatus;

                        System.Diagnostics.Debug.WriteLine($"처리 상태 확인 ({attempt + 1}/{maxAttempts}): Upload={uploadStatus}, Processing={processingStatus}");

                        // 업로드 상태 확인
                        if (uploadStatus == "processed" || uploadStatus == "uploaded")
                        {
                            System.Diagnostics.Debug.WriteLine($"비디오 처리 완료: {videoId}");
                            return true;
                        }

                        if (uploadStatus == "failed" || uploadStatus == "rejected")
                        {
                            throw new Exception($"YouTube에서 비디오 처리 실패: {uploadStatus}");
                        }

                        // 진행률 업데이트
                        int processingPercentage = 96 + (attempt * 4 / maxAttempts); // 96-100% 범위
                        progress?.Report(new UploadProgressInfo
                        {
                            BytesSent = 0,
                            TotalBytes = 0,
                            Percentage = Math.Min(99, processingPercentage),
                            Status = $"YouTube 처리 중 ({uploadStatus})",
                            ElapsedTime = TimeSpan.FromSeconds(attempt * delaySeconds),
                            VideoId = videoId
                        });
                    }

                    await Task.Delay(delaySeconds * 1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"처리 상태 확인 오류: {ex.Message}");
                    // 상태 확인 실패해도 계속 진행
                }
            }

            System.Diagnostics.Debug.WriteLine($"처리 상태 확인 시간 초과: {videoId}");
            return false; // 처리 상태 확인 실패 (하지만 업로드는 성공했을 가능성 있음)
        }

        // 업로드된 내 비디오 목록 가져오기
        public async Task<IList<Video>> GetMyVideosAsync(int maxResults = 10)
        {
            if (!IsAuthenticated())
            {
                throw new Exception("YouTube에 인증되지 않았습니다.");
            }

            try
            {
                var channelsListRequest = youtubeService.Channels.List("contentDetails");
                channelsListRequest.Mine = true;

                var channelsListResponse = await channelsListRequest.ExecuteAsync();
                var channel = channelsListResponse.Items[0];
                var uploadsListId = channel.ContentDetails.RelatedPlaylists.Uploads;

                var playlistItemsListRequest = youtubeService.PlaylistItems.List("snippet");
                playlistItemsListRequest.PlaylistId = uploadsListId;
                playlistItemsListRequest.MaxResults = maxResults;

                var playlistItemsListResponse = await playlistItemsListRequest.ExecuteAsync();

                var videos = new List<Video>();
                foreach (var playlistItem in playlistItemsListResponse.Items)
                {
                    var video = new Video
                    {
                        Id = playlistItem.Snippet.ResourceId.VideoId,
                        Snippet = new VideoSnippet
                        {
                            Title = playlistItem.Snippet.Title,
                            Description = playlistItem.Snippet.Description,
                            PublishedAtDateTimeOffset = playlistItem.Snippet.PublishedAtDateTimeOffset
                        }
                    };
                    videos.Add(video);
                }

                return videos;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"비디오 목록 가져오기 오류: {ex.Message}");
                throw new Exception($"비디오 목록을 가져오는 중 오류 발생: {ex.Message}");
            }
        }

        // 리소스 정리
        public void Dispose()
        {
            youtubeService?.Dispose();
        }
    }
}