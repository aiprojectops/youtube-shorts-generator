using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
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
    
        // 🔥 사용자 ID 추가
        private readonly string _userId;
        
        private YouTubeService youtubeService;
        private UserCredential credential;
    
        // 🔥 생성자에서 userId 받기
        public YouTubeUploader(string userId = null)
        {
            _userId = userId ?? Guid.NewGuid().ToString(); // userId 없으면 랜덤 생성
            Console.WriteLine($"=== YouTubeUploader 생성: UserId={_userId}");
        }

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
            public string VideoId { get; set; }
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

        // 웹 기반 인증을 위한 새로운 메서드
       public async Task<string> GetAuthorizationUrlAsync(string baseUrl, string returnPage = "youtube-upload")
        {
            try
            {
                var config = ConfigManager.GetConfig();
        
                if (string.IsNullOrEmpty(config.YouTubeClientId) || string.IsNullOrEmpty(config.YouTubeClientSecret))
                {
                    throw new Exception("YouTube API 클라이언트 ID와 시크릿이 설정되지 않았습니다.");
                }
        
                // 🔥 메모리 저장소 사용
                var dataStore = new MemoryDataStore(_userId);
        
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = config.YouTubeClientId,
                        ClientSecret = config.YouTubeClientSecret
                    },
                    Scopes = Scopes,
                    DataStore = dataStore  // 🔥 변경됨
                });
        
                string redirectUri;
                if (baseUrl.StartsWith("http://") && !baseUrl.Contains("localhost"))
                {
                    redirectUri = baseUrl.Replace("http://", "https://") + "/oauth/google/callback";
                }
                else
                {
                    redirectUri = $"{baseUrl.TrimEnd('/')}/oauth/google/callback";
                }
                
                Console.WriteLine($"=== GetAuthorizationUrlAsync 최종 리디렉션 URI: {redirectUri}");
                Console.WriteLine($"=== Return Page: {returnPage}");
                
                var request = flow.CreateAuthorizationCodeRequest(redirectUri);
                request.State = returnPage;
                
                var authUrl = request.Build().ToString();
                Console.WriteLine($"=== 생성된 인증 URL: {authUrl}");
        
                return authUrl;
            }
            catch (Exception ex)
            {
                throw new Exception($"인증 URL 생성 실패: {ex.Message}");
            }
        }
                      

        // 콜백에서 받은 코드로 토큰 교환
        public async Task<bool> ExchangeCodeForTokenAsync(string code, string baseUrl)
        {
            try
            {
                var config = ConfigManager.GetConfig();
                
                // 🔥 메모리 저장소 사용
                var dataStore = new MemoryDataStore(_userId);
                
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = config.YouTubeClientId,
                        ClientSecret = config.YouTubeClientSecret
                    },
                    Scopes = Scopes,
                    DataStore = dataStore  // 🔥 변경됨
                });
        
                string redirectUri;
                if (baseUrl.StartsWith("http://") && !baseUrl.Contains("localhost"))
                {
                    redirectUri = baseUrl.Replace("http://", "https://") + "/oauth/google/callback";
                }
                else
                {
                    redirectUri = $"{baseUrl.TrimEnd('/')}/oauth/google/callback";
                }
                
                Console.WriteLine($"=== ExchangeCodeForTokenAsync 최종 리디렉션 URI: {redirectUri}");
                
                var token = await flow.ExchangeCodeForTokenAsync("user", code, redirectUri, CancellationToken.None);
        
                credential = new UserCredential(flow, "user", token);
        
                youtubeService = new YouTubeService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });
        
                System.Diagnostics.Debug.WriteLine("토큰 교환 성공!");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"토큰 교환 실패: {ex.Message}");
                throw new Exception($"토큰 교환 실패: {ex.Message}");
            }
        }

        
        // 기존 토큰으로 인증 시도
        public async Task<bool> AuthenticateAsync(bool forceReauth = false)
        {
            try
            {
                if (forceReauth)
                {
                    credential = null;
                    youtubeService = null;
                }
        
                var config = ConfigManager.GetConfig();
        
                if (string.IsNullOrEmpty(config.YouTubeClientId) || string.IsNullOrEmpty(config.YouTubeClientSecret))
                {
                    throw new Exception("YouTube API 클라이언트 ID와 시크릿이 설정되지 않았습니다.");
                }
        
                // 🔥 메모리 저장소 사용
                var dataStore = new MemoryDataStore(_userId);
        
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = config.YouTubeClientId,
                        ClientSecret = config.YouTubeClientSecret
                    },
                    Scopes = Scopes,
                    DataStore = dataStore  // 🔥 변경됨
                });
        
                var token = await dataStore.GetAsync<TokenResponse>("user");
                
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    credential = new UserCredential(flow, "user", token);
        
                    youtubeService = new YouTubeService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = ApplicationName,
                    });
        
                    // 토큰 유효성 검사
                    try
                    {
                        var channelsRequest = youtubeService.Channels.List("snippet");
                        channelsRequest.Mine = true;
                        channelsRequest.MaxResults = 1;
                        await channelsRequest.ExecuteAsync();
                        
                        System.Diagnostics.Debug.WriteLine("기존 토큰으로 인증 성공!");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"기존 토큰 유효하지 않음: {ex.Message}");
                    }
                }
        
                System.Diagnostics.Debug.WriteLine("새로운 인증이 필요합니다.");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"인증 확인 실패: {ex.Message}");
                return false;
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
                    VideoCount = channel.Statistics?.VideoCount ?? 0,
                    Email = "YouTube 계정"
                };

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
            return false; // 웹에서는 다시 인증 URL을 받아서 처리해야 함
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

        // 비디오 파일 검증 메서드
        private bool ValidateVideoFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }

                var fileInfo = new FileInfo(filePath);

                // 파일 크기 검사 (2GB로 제한)
                const long maxSize = 2L * 1024 * 1024 * 1024;
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
                        if (!string.IsNullOrEmpty(trimmedTag) && trimmedTag.Length <= 500)
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

                videoMetadata.Status.SelfDeclaredMadeForKids = false;

                string uploadedVideoId = null;

                using (var fileStream = new FileStream(uploadInfo.FilePath, FileMode.Open, FileAccess.Read))
                {
                    var videosInsertRequest = youtubeService.Videos.Insert(videoMetadata, "snippet,status", fileStream, "video/*");
                    videosInsertRequest.ChunkSize = ResumableUpload.MinimumChunkSize * 4;

                    DateTime startTime = DateTime.Now;
                    long totalBytes = fileStream.Length;

                    videosInsertRequest.ResponseReceived += (uploadedVideo) =>
                    {
                        uploadedVideoId = uploadedVideo.Id;
                        System.Diagnostics.Debug.WriteLine($"업로드 완료: 비디오 ID = {uploadedVideoId}");
                    };

                    var progressTimer = new System.Timers.Timer(1000);
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

                        progress?.Report(new UploadProgressInfo
                        {
                            BytesSent = totalBytes,
                            TotalBytes = totalBytes,
                            Percentage = 100,
                            Status = "업로드 완료",
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

        /// <summary>
        /// 현재 Credential 반환
        /// </summary>
        public UserCredential GetCredential()
        {
            return credential;
        }

        /// <summary>
        /// Refresh Token으로 직접 인증 (스케줄 업로드용)
        /// </summary>
        public async Task<bool> AuthenticateWithRefreshTokenAsync(string refreshToken)
        {
            try
            {
                if (string.IsNullOrEmpty(refreshToken))
                {
                    Console.WriteLine("=== Refresh Token이 없습니다");
                    return false;
                }
        
                var config = ConfigManager.GetConfig();
                
                if (string.IsNullOrEmpty(config.YouTubeClientId) || 
                    string.IsNullOrEmpty(config.YouTubeClientSecret))
                {
                    throw new Exception("YouTube API 설정이 없습니다");
                }
        
                Console.WriteLine($"=== Refresh Token으로 인증 시작: UserId={_userId}");
        
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = config.YouTubeClientId,
                        ClientSecret = config.YouTubeClientSecret
                    },
                    Scopes = Scopes,
                    DataStore = null
                });
        
                var token = new TokenResponse
                {
                    RefreshToken = refreshToken
                };
        
                credential = new UserCredential(flow, "user", token);
        
                // Access Token 갱신
                bool tokenRefreshed = await credential.RefreshTokenAsync(CancellationToken.None);
                
                if (!tokenRefreshed)
                {
                    throw new Exception("Access Token 갱신 실패");
                }
        
                youtubeService = new YouTubeService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });
        
                // 유효성 검사
                var channelsRequest = youtubeService.Channels.List("snippet");
                channelsRequest.Mine = true;
                channelsRequest.MaxResults = 1;
                await channelsRequest.ExecuteAsync();
                
                Console.WriteLine("=== Refresh Token 인증 성공!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== Refresh Token 인증 실패: {ex.Message}");
                return false;
            }
        }

        
        // 리소스 정리
        public void Dispose()
        {
            youtubeService?.Dispose();
        }
    }

}
