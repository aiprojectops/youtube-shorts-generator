using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YouTubeShortsWebApp
{
    public class RunwayClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://api.runwayml.com/v1";

        public RunwayClient(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
        }

        public class VideoGenerationRequest
        {
            public string prompt { get; set; }
            public string image { get; set; } = null; // optional for image-to-video
            public int duration { get; set; } = 5;
            public string resolution { get; set; } = "1280x768";
            public string aspect_ratio { get; set; } = "16:9";
            public string model { get; set; } = "gen3a_turbo";
            public bool watermark { get; set; } = false;
        }

        public class TaskResponse
        {
            public string id { get; set; }
            public string status { get; set; }
            public List<TaskOutput> output { get; set; }
            public string failure { get; set; }
            public string failure_code { get; set; }
            public DateTime created_at { get; set; }
            public DateTime? completed_at { get; set; }
        }

        public class TaskOutput
        {
            public string url { get; set; }
            public string filename { get; set; }
        }

        // 진행률 정보를 담는 클래스
        public class ProgressInfo
        {
            public int Percentage { get; set; }
            public string Status { get; set; }
            public TimeSpan ElapsedTime { get; set; }
            public string EstimatedTimeRemaining { get; set; }
        }

        // 비디오 생성 시작
        public async Task<TaskResponse> StartVideoGeneration(VideoGenerationRequest request)
        {
            try
            {
                // 화면비율에 따른 해상도 설정
                request.resolution = GetResolutionFromAspectRatio(request.aspect_ratio);
                
                // 항상 같은 구조를 유지하도록 수정
                var requestBody = new
                {
                    model = request.model,
                    prompt = request.prompt,
                    image = !string.IsNullOrEmpty(request.image) ? request.image : (string?)null,
                    duration = request.duration,
                    resolution = request.resolution,
                    watermark = request.watermark
                };
        
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody, Newtonsoft.Json.Formatting.Indented);
                System.Diagnostics.Debug.WriteLine("=== RunwayML API 요청 ===");
                System.Diagnostics.Debug.WriteLine($"URL: {BaseUrl}/tasks");
                System.Diagnostics.Debug.WriteLine("JSON:");
                System.Diagnostics.Debug.WriteLine(json);
                
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync($"{BaseUrl}/tasks", content);
                string responseContent = await response.Content.ReadAsStringAsync();
                
                System.Diagnostics.Debug.WriteLine("=== RunwayML API 응답 ===");
                System.Diagnostics.Debug.WriteLine($"상태 코드: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine("응답 내용:");
                System.Diagnostics.Debug.WriteLine(responseContent);
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"RunwayML API 요청 실패: {response.StatusCode} - {responseContent}");
                }
                
                return Newtonsoft.Json.JsonConvert.DeserializeObject<TaskResponse>(responseContent);
            }
            catch (Exception ex)
            {
                throw new Exception($"RunwayML 비디오 생성 요청 중 오류 발생: {ex.Message}");
            }
        }

        // 작업 상태 확인
        public async Task<TaskResponse> GetTaskStatus(string taskId)
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{BaseUrl}/tasks/{taskId}");
                string responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"RunwayML 상태 확인: {taskId} - {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"RunwayML 상태 확인 실패: {response.StatusCode} - {responseContent}");
                }

                return Newtonsoft.Json.JsonConvert.DeserializeObject<TaskResponse>(responseContent);
            }
            catch (Exception ex)
            {
                throw new Exception($"RunwayML 상태 확인 중 오류 발생: {ex.Message}");
            }
        }

        // 비디오 생성 완료까지 대기
        public async Task<TaskResponse> WaitForCompletion(string taskId,
            IProgress<ProgressInfo> progress = null, CancellationToken cancellationToken = default)
        {
            const int maxAttempts = 200; // 약 16-17분 (5초 간격)
            int attempts = 0;
            DateTime startTime = DateTime.Now;

            while (attempts < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                var status = await GetTaskStatus(taskId);

                // 진행률 계산 및 업데이트
                var progressInfo = CalculateProgress(status, startTime, attempts, maxAttempts);
                progress?.Report(progressInfo);

                System.Diagnostics.Debug.WriteLine($"RunwayML 시도 {attempts + 1}/{maxAttempts}: {status.status} - {progressInfo.Percentage}%");

                if (status.status == "SUCCEEDED")
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
                else if (status.status == "FAILED")
                {
                    string errorMsg = status.failure ?? status.failure_code ?? "알 수 없는 오류";
                    throw new Exception($"RunwayML 비디오 생성 실패: {errorMsg}");
                }

                await Task.Delay(5000, cancellationToken); // 5초 대기
                attempts++;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("사용자에 의해 취소되었습니다.");
            }

            throw new TimeoutException($"RunwayML 비디오 생성 시간이 초과되었습니다. (최대 {maxAttempts * 5 / 60}분)");
        }

        // 진행률 계산
        private ProgressInfo CalculateProgress(TaskResponse status, DateTime startTime, int attemptNumber, int maxAttempts)
        {
            var elapsed = DateTime.Now - startTime;
            int percentage = 0;
            string statusText = status.status;
            string estimatedTimeRemaining = "계산 중...";

            switch (status.status?.ToUpper())
            {
                case "PENDING":
                    percentage = 5;
                    statusText = "대기 중";
                    break;
                case "RUNNING":
                    // 실행 중일 때 시간 기반으로 진행률 추정
                    double runningProgress = Math.Min(90, (attemptNumber * 100.0 / maxAttempts) * 0.8 + 10);
                    percentage = (int)runningProgress;
                    statusText = "비디오 생성 중";

                    // 남은 시간 추정
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
                case "SUCCEEDED":
                    percentage = 100;
                    statusText = "완료됨";
                    estimatedTimeRemaining = "완료";
                    break;
                case "FAILED":
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

        // 화면비율에 따른 해상도 결정
        private string GetResolutionFromAspectRatio(string aspectRatio)
        {
            return aspectRatio switch
            {
                "9:16" => "768x1280",      // 세로 (쇼츠)
                "16:9" => "1280x768",      // 가로
                "1:1" => "1024x1024",      // 정사각형
                _ => "1280x768"            // 기본값
            };
        }

        // 리소스 정리
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
