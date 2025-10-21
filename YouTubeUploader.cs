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
        
        // 🔥 동시 실행 제한 (메모리 보호)
        private static readonly SemaphoreSlim _uploadSemaphore = new SemaphoreSlim(1, 1);
        
        private YouTubeService youtubeService;
        private UserCredential credential;

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

        public class UploadProgressInfo
        {
            public long BytesSent { get; set; }
            public long TotalBytes { get; set; }
            public int Percentage { get; set; }
            public string Status { get; set; }
            public TimeSpan ElapsedTime { get; set; }
            public string VideoId { get; set; }
        }

        public class VideoUploadInfo
        {
            public string FilePath { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string Tags { get; set; }
            public string PrivacyStatus { get; set; }
        }

        // 🔥 Thread-safe 파일 저장소 래퍼
        private class SafeFileDataStore : IDataStore
        {
            private readonly string _folder;
            private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

            public SafeFileDataStore(string folder)
            {
                _folder = folder;
                Directory.CreateDirectory(_folder);
            }

            public async Task StoreAsync<T>(string key, T value)
            {
                await _lock.WaitAsync();
                try
                {
                    string filePath = Path.Combine(_folder, key);
                    string json = System.Text.Json.JsonSerializer.Serialize(value);
                    
                    // 🔥 임시 파일에 쓰고 원자적으로 이동
                    string tempPath = filePath + ".tmp";
                    await File.WriteAllTextAsync(tempPath, json);
                    File.Move(tempPath, filePath, true);
                }
                finally
                {
                    _lock.Release();
                }
            }

            public async Task DeleteAsync<T>(string key)
            {
                await _lock.WaitAsync();
                try
                {
                    string filePath = Path.Combine(_folder, key);
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }

            public async Task<T> GetAsync<T>(string key)
            {
                await _lock.WaitAsync();
                try
                {
                    string filePath = Path.Combine(_folder, key);
                    if (!File.Exists(filePath))
                    {
                        return default(T);
                    }

                    string json = await File.ReadAllTextAsync(filePath);
                    return System.Text.Json.JsonSerializer.Deserialize<T>(json);
                }
                finally
                {
                    _lock.Release();
                }
            }

            public async Task ClearAsync()
            {
                await _lock.WaitAsync();
                try
                {
                    if (Directory.Exists(_folder))
                    {
                        foreach (var file in Directory.GetFiles(_folder))
                        {
                            File.Delete(file);
                        }
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
        }

        public async Task<string> GetAuthorizationUrlAsync(string baseUrl, string returnPage = "youtube-upload")
        {
            try
            {
                var config = ConfigManager.GetConfig();
        
                if (string.IsNullOrEmpty(config.YouTubeClientId) || string.IsNullOrEmpty(config.YouTubeClientSecret))
                {
                    throw new Exception("YouTube API 클라이언트 ID와 시크릿이 설정되지 않았습니다.");
                }
        
                // 🔥 안전한 파일 저장소 사용
                var dataStore = new SafeFileDataStore("/tmp/youtube_tokens");
        
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = config.YouTubeClientId,
                        ClientSecret = config.YouTubeClientSecret
                    },
                    Scopes = Scopes,
                    DataStore = dataStore
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
                
                Console.WriteLine($"=== 인증 URL 생성: {redirectUri}");
                
                var request = flow.CreateAuthorizationCodeRequest(redirectUri);
                request.State = returnPage;
                
                return request.Build().ToString();
            }
            catch (Exception ex)
            {
                throw new Exception($"인증 URL 생성 실패: {ex.Message}");
            }
        }

        public async Task<bool> ExchangeCodeForTokenAsync(string code, string baseUrl)
        {
            try
            {
                var config = ConfigManager.GetConfig();
                var dataStore = new SafeFileDataStore("/tmp/youtube_tokens");
                
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = config.YouTubeClientId,
                        ClientSecret = config.YouTubeClientSecret
                    },
                    Scopes = Scopes,
                    DataStore = dataStore
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
                
                Console.WriteLine($"=== 토큰 교환: {redirectUri}");
                
                var token = await flow.ExchangeCodeForTokenAsync("user", code, redirectUri, CancellationToken.None);
        
                credential = new UserCredential(flow, "user", token);
        
                youtubeService = new YouTubeService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });
        
                Console.WriteLine("✅ 토큰 교환 성공");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 토큰 교환 실패: {ex.Message}");
                throw;
            }
        }

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
        
                if (string.IsNullOrEmpty(config.YouTubeClientId) || 
                    string.IsNullOrEmpty(config.YouTubeClientSecret))
                {
                    return false;
                }
        
                var dataStore = new SafeFileDataStore("/tmp/youtube_tokens");
        
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = config.YouTubeClientId,
                        ClientSecret = config.YouTubeClientSecret
                    },
                    Scopes = Scopes,
                    DataStore = dataStore
                });
        
                var token = await dataStore.GetAsync<TokenResponse>("user");
                
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    // 🔥 토큰 만료 확인 및 자동 갱신
                    if (token.IssuedUtc.AddSeconds(token.ExpiresInSeconds ?? 3600) < DateTime.UtcNow)
                    {
                        if (!string.IsNullOrEmpty(token.RefreshToken))
                        {
                            try
                            {
                                var newToken = await flow.RefreshTokenAsync("user", token.RefreshToken, CancellationToken.None);
                                token = newToken;
                                Console.WriteLine("✅ 토큰 자동 갱신 성공");
                            }
                            catch
                            {
                                return false;
                            }
                        }
                        else
                        {
                            return false;
                        }
                    }
                    
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
                        
                        Console.WriteLine("✅ 기존 토큰 인증 성공");
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
        
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 인증 실패: {ex.Message}");
                return false;
            }
        }

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

                return new YouTubeAccountInfo
                {
                    ChannelTitle = channel.Snippet.Title,
                    ChannelId = channel.Id,
                    ThumbnailUrl = channel.Snippet.Thumbnails?.Default__?.Url,
                    ChannelUrl = $"https://www.youtube.com/channel/{channel.Id}",
                    SubscriberCount = channel.Statistics?.SubscriberCount ?? 0,
                    VideoCount = channel.Statistics?.VideoCount ?? 0,
                    Email = "YouTube 계정"
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"계정 정보 가져오기 실패: {ex.Message}");
            }
        }

        public bool IsAuthenticated()
        {
            return youtubeService != null && credential != null;
        }

        public async Task<bool> SwitchAccountAsync()
        {
            await RevokeAuthenticationAsync();
            return false;
        }

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

                Console.WriteLine("✅ 인증 해제 완료");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 인증 해제 실패: {ex.Message}");
            }
        }

        private bool ValidateVideoFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }

                var fileInfo = new FileInfo(filePath);

                const long maxSize = 2L * 1024 * 1024 * 1024;
                if (fileInfo.Length > maxSize)
                {
                    Console.WriteLine($"파일 크기 초과: {fileInfo.Length / 1024 / 1024}MB");
                    return false;
                }

                string extension = fileInfo.Extension.ToLower();
                var allowedExtensions = new[] { ".mp4", ".mov", ".avi", ".wmv", ".flv", ".webm", ".mkv" };

                return Array.Exists(allowedExtensions, ext => ext == extension);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"파일 검증 오류: {ex.Message}");
                return false;
            }
        }

        // 🔥 메모리 보호를 위한 업로드 메서드
        public async Task<string> UploadVideoAsync(VideoUploadInfo uploadInfo, IProgress<UploadProgressInfo> progress = null, CancellationToken cancellationToken = default)
        {
            // 🔥 동시 업로드 제한 (메모리 보호)
            await _uploadSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                if (!IsAuthenticated())
                {
                    throw new Exception("YouTube 인증 필요");
                }

                if (!ValidateVideoFile(uploadInfo.FilePath))
                {
                    throw new Exception($"유효하지 않은 파일: {uploadInfo.FilePath}");
                }

                Console.WriteLine($"⬆️ 업로드 시작: {Path.GetFileName(uploadInfo.FilePath)}");

                var videoMetadata = new Video();
                videoMetadata.Snippet = new VideoSnippet();
                videoMetadata.Snippet.Title = uploadInfo.Title;
                videoMetadata.Snippet.Description = uploadInfo.Description;
                videoMetadata.Snippet.CategoryId = "22";

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

                videoMetadata.Status = new VideoStatus();
                switch (uploadInfo.PrivacyStatus?.ToLower())
                {
                    case "공개":
                        videoMetadata.Status.PrivacyStatus = "public";
                        break;
                    case "링크 공유":
                    case "목록에 없음":
                        videoMetadata.Status.PrivacyStatus = "unlisted";
                        break;
                    default:
                        videoMetadata.Status.PrivacyStatus = "private";
                        break;
                }

                videoMetadata.Status.SelfDeclaredMadeForKids = false;

                string uploadedVideoId = null;

                // 🔥 파일 스트림을 using으로 안전하게 관리
                using (var fileStream = new FileStream(uploadInfo.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920))
                {
                    var videosInsertRequest = youtubeService.Videos.Insert(videoMetadata, "snippet,status", fileStream, "video/*");
                    
                    // 🔥 작은 청크 크기로 메모리 절약
                    videosInsertRequest.ChunkSize = ResumableUpload.MinimumChunkSize * 2;

                    DateTime startTime = DateTime.Now;
                    long totalBytes = fileStream.Length;

                    videosInsertRequest.ResponseReceived += (uploadedVideo) =>
                    {
                        uploadedVideoId = uploadedVideo.Id;
                        Console.WriteLine($"✅ 업로드 완료: {uploadedVideoId}");
                    };

                    var uploadResult = await videosInsertRequest.UploadAsync(cancellationToken);

                    if (uploadResult.Status == UploadStatus.Failed)
                    {
                        string errorMessage = uploadResult.Exception?.Message ?? "알 수 없는 오류";
                        throw new Exception($"업로드 실패: {errorMessage}");
                    }

                    if (uploadResult.Status != UploadStatus.Completed)
                    {
                        throw new Exception($"업로드 미완료: {uploadResult.Status}");
                    }

                    if (string.IsNullOrEmpty(uploadedVideoId))
                    {
                        throw new Exception("비디오 ID 없음");
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
                    Console.WriteLine($"🎬 YouTube URL: {videoUrl}");

                    return videoUrl;
                }
            }
            finally
            {
                _uploadSemaphore.Release();
                
                // 🔥 가비지 컬렉션 강제 실행 (메모리 확보)
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        public void Dispose()
        {
            youtubeService?.Dispose();
        }
    }
}

    // 메모리 데이터 저장소 클래스
    public class MemoryDataStore : IDataStore
    {
        private static readonly ConcurrentDictionary<string, object> _store = new();

        public Task StoreAsync<T>(string key, T value)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync<T>(string key)
        {
            _store.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(string key)
        {
            if (_store.TryGetValue(key, out object value) && value is T)
            {
                return Task.FromResult((T)value);
            }
            return Task.FromResult(default(T));
        }

        public Task ClearAsync()
        {
            _store.Clear();
            return Task.CompletedTask;
        }
    }
}
