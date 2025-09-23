using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YouTubeShortsWebApp
{
    public class ReplicateClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://api.replicate.com/v1";
        private const string ModelPath = "bytedance/seedance-1-pro";

        public ReplicateClient(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            _httpClient.Timeout = TimeSpan.FromMinutes(30); // HTTP 클라이언트 타임아웃 증가
        }

        public class VideoGenerationRequest
        {
            public string prompt { get; set; }
            public string image { get; set; } = null; // optional for image-to-video
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
            public object logs { get; set; } // 로그 정보 추가
        }

        // 진행률 정보를 담는 클래스
        public class ProgressInfo
        {
            public int Percentage { get; set; }
            public string Status { get; set; }
            public TimeSpan ElapsedTime { get; set; }
            public string EstimatedTimeRemaining { get; set; }
        }

        // 영상 생성 시작 - 공식 API 형식
        public async Task<PredictionResponse> StartVideoGeneration(VideoGenerationRequest request)
        {
            try
            {
                // null 값들을 제거하여 정리된 input 객체 생성
                var input = new Dictionary<string, object>
                {
                    ["prompt"] = request.prompt,
                    ["duration"] = request.duration,
                    ["resolution"] = request.resolution,
                    ["aspect_ratio"] = request.aspect_ratio,
                    ["fps"] = request.fps,
                    ["camera_fixed"] = request.camera_fixed
                };

                // null이 아닌 값들만 추가
                if (!string.IsNullOrEmpty(request.image))
                {
                    input["image"] = request.image;
                }

                if (request.seed.HasValue)
                {
                    input["seed"] = request.seed.Value;
                }

                // 공식 API 형식: input 객체만 전달
                var requestBody = new
                {
                    input = input
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody, Newtonsoft.Json.Formatting.Indented);

                // 디버깅용
                System.Diagnostics.Debug.WriteLine("=== API 요청 ===");
                System.Diagnostics.Debug.WriteLine($"URL: {BaseUrl}/models/{ModelPath}/predictions");
                System.Diagnostics.Debug.WriteLine("JSON:");
                System.Diagnostics.Debug.WriteLine(json);

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // 공식 API 경로 사용
                HttpResponseMessage response = await _httpClient.PostAsync($"{BaseUrl}/models/{ModelPath}/predictions", content);
                string responseContent = await response.Content.ReadAsStringAsync();

                // 디버깅용
                System.Diagnostics.Debug.WriteLine("=== API 응답 ===");
                System.Diagnostics.Debug.WriteLine($"상태 코드: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine("응답 내용:");
                System.Diagnostics.Debug.WriteLine(responseContent);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API 요청 실패: {response.StatusCode} - {responseContent}");
                }

                return Newtonsoft.Json.JsonConvert.DeserializeObject<PredictionResponse>(responseContent);
            }
            catch (Exception ex)
            {
                throw new Exception($"영상 생성 요청 중 오류 발생: {ex.Message}");
            }
        }

        // 생성 상태 확인
        public async Task<PredictionResponse> GetPredictionStatus(string predictionId)
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{BaseUrl}/predictions/{predictionId}");
                string responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"상태 확인: {predictionId} - {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"상태 확인 실패: {response.StatusCode} - {responseContent}");
                }

                return Newtonsoft.Json.JsonConvert.DeserializeObject<PredictionResponse>(responseContent);
            }
            catch (Exception ex)
            {
                throw new Exception($"상태 확인 중 오류 발생: {ex.Message}");
            }
        }

        // 개선된 진행률 계산
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
                    // 처리 단계에서는 시간 기반으로 진행률 추정
                    // 이미지-투-비디오는 더 오래 걸리므로 다르게 계산
                    double processingProgress = Math.Min(90, (attemptNumber * 100.0 / maxAttempts) * 0.8 + 10);
                    percentage = (int)processingProgress;
                    statusText = "영상 생성 중";

                    // 남은 시간 추정 (대략적)
                    if (attemptNumber > 5) // 충분한 데이터가 있을 때만
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

        // 영상 생성 완료까지 대기 (폴링) - 개선된 버전
        public async Task<PredictionResponse> WaitForCompletion(string predictionId,
            IProgress<ProgressInfo> progress = null, CancellationToken cancellationToken = default)
        {
            // 최대 대기 시간을 20분으로 증가 (이미지-투-비디오는 더 오래 걸림)
            const int maxAttempts = 240; // 20분 (5초 간격)
            int attempts = 0;
            DateTime startTime = DateTime.Now;

            // 첫 번째 몇 번의 체크는 더 자주 하기 (1초 간격)
            int quickCheckCount = 10;

            while (attempts < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                var status = await GetPredictionStatus(predictionId);

                // 진행률 계산 및 업데이트
                var progressInfo = CalculateProgress(status, startTime, attempts, maxAttempts);
                progress?.Report(progressInfo);

                System.Diagnostics.Debug.WriteLine($"시도 {attempts + 1}/{maxAttempts}: {status.status} - {progressInfo.Percentage}%");

                if (status.status == "succeeded")
                {
                    // 최종 완료 진행률 업데이트
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

                // 대기 시간 조정 (처음에는 1초, 나중에는 5초)
                int delaySeconds = attempts < quickCheckCount ? 1 : 5;
                await Task.Delay(delaySeconds * 1000, cancellationToken);
                attempts++;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("사용자에 의해 취소되었습니다.");
            }

            throw new TimeoutException($"영상 생성 시간이 초과되었습니다. (최대 {maxAttempts * 5 / 60}분)");
        }


        // ReplicateClient.cs에 추가할 메서드들

        public class AccountInfo
        {
            public decimal? credit_balance { get; set; }
            public string username { get; set; } = "";
            public string type { get; set; } = "";
        }

        // 계정 정보 및 크레딧 잔액 조회
        // 계정 정보 및 크레딧 잔액 조회
        public async Task<AccountInfo> GetAccountInfoAsync()
        {
            try
            {
                // 다양한 엔드포인트 시도
                string[] endpoints = {
            $"{BaseUrl}/account",
            $"{BaseUrl}/user",
            $"{BaseUrl}/billing/balance"
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
                return null; // 오류 시 null 반환
            }
        }


        // 리소스 정리
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}