using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq; // Take 메서드를 위해

namespace YouTubeShortsWebApp
{
    public class ReplicateClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private const string ModelPath = "bytedance/seedance-1-pro";

        public ReplicateClient(string apiKey)
        {
            _apiKey = apiKey;
            
            string proxyUrl = Environment.GetEnvironmentVariable("REPLICATE_PROXY_URL");
            
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                _baseUrl = $"{proxyUrl.TrimEnd('/')}/api/replicate/v1";
                // Console.WriteLine($"=== Replicate 프록시 사용: {_baseUrl}"); // 제거
            }
            else
            {
                _baseUrl = "https://api.replicate.com/v1";
                // Console.WriteLine($"=== Replicate 직접 접근: {_baseUrl}"); // 제거
            }
            
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            
            _httpClient = new HttpClient(handler);
            
            // Console.WriteLine("=== ReplicateClient 초기화 - Cloudflare 우회 헤더 설정"); // 제거
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            _httpClient.DefaultRequestHeaders.Add("Pragma", "no-cache");
            _httpClient.DefaultRequestHeaders.Add("Sec-Ch-Ua", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"");
            _httpClient.DefaultRequestHeaders.Add("Sec-Ch-Ua-Mobile", "?0");
            _httpClient.DefaultRequestHeaders.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
            
            // Console.WriteLine("=== ReplicateClient 헤더 설정 완료"); // 제거
        }

        public class VideoGenerationRequest
        {
            public string prompt { get; set; }
            public string image { get; set; } = null;
            public int duration { get; set; } = 5;
            public string resolution { get; set; } = "1080p";
            public string aspect_ratio { get; set; } = "16:9";
            public int fps { get; set; } = 24;
            public bool camera_fixed { get; set; } = false;
            public int? seed { get; set; } = null;
        }

        public class PredictionResponse
        {
            public string id { get; set; }
            public string status { get; set; }
            public object output { get; set; }
            public string error { get; set; }
            public DateTime created_at { get; set; }
            public DateTime? completed_at { get; set; }
            public object logs { get; set; }
        }

        public class ProgressInfo
        {
            public int Percentage { get; set; }
            public string Status { get; set; }
            public TimeSpan ElapsedTime { get; set; }
            public string EstimatedTimeRemaining { get; set; }
        }

        // 연결 테스트 메서드
        public async Task<bool> TestConnectionAsync()
        {
            try
            {              
                var response = await _httpClient.GetAsync($"{_baseUrl}/models");
                var content = await response.Content.ReadAsStringAsync();         
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Replicate 연결 실패: {ex.Message}");
                return false;
            }
        }

       public async Task<PredictionResponse> StartVideoGeneration(VideoGenerationRequest request)
        {
            try
            {
                await Task.Delay(3000);
               
                var input = new Dictionary<string, object>
                {
                    ["prompt"] = request.prompt,
                    ["duration"] = request.duration,
                    ["resolution"] = request.resolution,
                    ["aspect_ratio"] = request.aspect_ratio,
                    ["fps"] = request.fps,
                    ["camera_fixed"] = request.camera_fixed
                };
        
                if (!string.IsNullOrEmpty(request.image))
                {
                    input["image"] = request.image;
                }
        
                if (request.seed.HasValue)
                {
                    input["seed"] = request.seed.Value;
                }
        
                var requestBody = new { input = input };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody, Newtonsoft.Json.Formatting.Indented);
        
                // Debug 로그는 유지 (개발 환경에서만 보임)
                System.Diagnostics.Debug.WriteLine("=== API 요청 ===");
                System.Diagnostics.Debug.WriteLine($"URL: {_baseUrl}/models/{ModelPath}/predictions");
                System.Diagnostics.Debug.WriteLine("JSON:");
                System.Diagnostics.Debug.WriteLine(json);
        
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync($"{_baseUrl}/models/{ModelPath}/predictions", content);
                string responseContent = await response.Content.ReadAsStringAsync();
        
                System.Diagnostics.Debug.WriteLine("=== API 응답 ===");
                System.Diagnostics.Debug.WriteLine($"상태 코드: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine("응답 내용:");
                System.Diagnostics.Debug.WriteLine(responseContent);
        
                if (!response.IsSuccessStatusCode)
                {
                    // 오류 시에만 Console 출력
                    Console.WriteLine($"❌ Replicate API 오류: {response.StatusCode}");
                    
                    throw new Exception($"API 요청 실패: {response.StatusCode} - {responseContent}");
                }
        
                return Newtonsoft.Json.JsonConvert.DeserializeObject<PredictionResponse>(responseContent);
            }
            catch (Exception ex)
            {
                throw new Exception($"영상 생성 요청 중 오류 발생: {ex.Message}");
            }
        }

        
        public async Task<PredictionResponse> GetPredictionStatus(string predictionId)
        {
            try
            {
                await Task.Delay(2000);
                
                string requestUrl = $"{_baseUrl}/predictions/{predictionId}";
                // Console.WriteLine($"=== 상태 확인 요청 URL: {requestUrl}"); // 제거
                
                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
                string responseContent = await response.Content.ReadAsStringAsync();
                        
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"상태 확인 실패: {response.StatusCode} - {responseContent}");
                }
        
                var trimmedContent = responseContent.Trim();
                if (!trimmedContent.StartsWith("{"))
                {
                    throw new Exception($"유효하지 않은 JSON 응답: {trimmedContent.Substring(0, Math.Min(100, trimmedContent.Length))}");
                }
        
                return Newtonsoft.Json.JsonConvert.DeserializeObject<PredictionResponse>(responseContent);
            }
            catch (Exception ex)
            {
                throw new Exception($"상태 확인 중 오류 발생: {ex.Message}");
            }
        }


     
        private ProgressInfo CalculateProgress(PredictionResponse status, DateTime startTime, int attemptNumber, int maxAttempts)
        {
            var elapsed = DateTime.Now - startTime;
            int percentage = 0;
            string statusText = status.status;
            string estimatedTimeRemaining = "계산 중...";

            switch (status.status)
            {
                case "starting":
                    percentage = 5;
                    statusText = "초기화 중";
                    break;
                case "processing":
                    double processingProgress = Math.Min(90, (attemptNumber * 100.0 / maxAttempts) * 0.8 + 10);
                    percentage = (int)processingProgress;
                    statusText = "영상 생성 중";

                    if (attemptNumber > 5)
                    {
                        double avgTimePerAttempt = elapsed.TotalSeconds / attemptNumber;
                        double estimatedTotalTime = avgTimePerAttempt * maxAttempts;
                        double remainingTime = estimatedTotalTime - elapsed.TotalSeconds;

                        if (remainingTime > 0)
                        {
                            if (remainingTime > 60)
                                estimatedTimeRemaining = $"약 {(int)(remainingTime / 60)}분 {(int)(remainingTime % 60)}초";
                            else
                                estimatedTimeRemaining = $"약 {(int)remainingTime}초";
                        }
                    }
                    break;
                case "succeeded":
                    percentage = 100;
                    statusText = "완료됨";
                    estimatedTimeRemaining = "완료";
                    break;
                case "failed":
                    percentage = 0;
                    statusText = "실패";
                    estimatedTimeRemaining = "실패";
                    break;
                default:
                    percentage = (int)((attemptNumber * 100.0) / maxAttempts);
                    break;
            }

            return new ProgressInfo
            {
                Percentage = percentage,
                Status = statusText,
                ElapsedTime = elapsed,
                EstimatedTimeRemaining = estimatedTimeRemaining
            };
        }

        public async Task<PredictionResponse> WaitForCompletion(string predictionId,
            IProgress<ProgressInfo> progress = null, CancellationToken cancellationToken = default)
        {
            const int maxAttempts = 240;
            int attempts = 0;
            DateTime startTime = DateTime.Now;
            int quickCheckCount = 10;

            while (attempts < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                var status = await GetPredictionStatus(predictionId);
                var progressInfo = CalculateProgress(status, startTime, attempts, maxAttempts);
                progress?.Report(progressInfo);

                System.Diagnostics.Debug.WriteLine($"시도 {attempts + 1}/{maxAttempts}: {status.status} - {progressInfo.Percentage}%");

                if (status.status == "succeeded")
                {
                    progress?.Report(new ProgressInfo
                    {
                        Percentage = 100,
                        Status = "완료됨",
                        ElapsedTime = DateTime.Now - startTime,
                        EstimatedTimeRemaining = "완료"
                    });
                    return status;
                }
                else if (status.status == "failed" || status.status == "canceled")
                {
                    throw new Exception($"영상 생성 실패: {status.error ?? "알 수 없는 오류"}");
                }

                int delaySeconds = attempts < quickCheckCount ? 2 : 5; // 프록시 사용시 더 긴 간격
                await Task.Delay(delaySeconds * 1000, cancellationToken);
                attempts++;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("사용자에 의해 취소되었습니다.");
            }

            throw new TimeoutException($"영상 생성 시간이 초과되었습니다. (최대 {maxAttempts * 5 / 60}분)");
        }

        // 계정 정보 클래스
        public class AccountInfo
        {
            public decimal? credit_balance { get; set; }
            public string username { get; set; } = "";
            public string type { get; set; } = "";
        }

        // 계정 정보 및 크레딧 잔액 조회
        public async Task<AccountInfo> GetAccountInfoAsync()
        {
            try
            {
                string[] endpoints = {
                    $"{_baseUrl}/account",
                    $"{_baseUrl}/user",
                    $"{_baseUrl}/billing/balance"
                };

                foreach (string endpoint in endpoints)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"시도 중인 엔드포인트: {endpoint}");

                        HttpResponseMessage response = await _httpClient.GetAsync(endpoint);
                        string responseContent = await response.Content.ReadAsStringAsync();

                        System.Diagnostics.Debug.WriteLine($"응답 상태: {response.StatusCode}");
                        System.Diagnostics.Debug.WriteLine($"응답 내용: {responseContent}");

                        if (response.IsSuccessStatusCode)
                        {
                            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<AccountInfo>(responseContent);
                            if (result != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"성공한 엔드포인트: {endpoint}");
                                return result;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"엔드포인트 {endpoint} 실패: {ex.Message}");
                    }
                }

                throw new Exception("모든 계정 정보 엔드포인트에서 실패했습니다.");
            }
            catch (Exception ex)
            {
                throw new Exception($"계정 정보 조회 중 오류 발생: {ex.Message}");
            }
        }

        // 크레딧 잔액만 간단히 조회
        public async Task<decimal?> GetCreditBalanceAsync()
        {
            try
            {
                var accountInfo = await GetAccountInfoAsync();
                return accountInfo.credit_balance;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"크레딧 조회 오류: {ex.Message}");
                return null;
            }
        }

        // 리소스 정리
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
