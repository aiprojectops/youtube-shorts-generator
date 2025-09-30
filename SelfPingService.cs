using Microsoft.Extensions.Hosting;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace YouTubeShortsWebApp
{
    public class SelfPingService : BackgroundService
    {
        private readonly ILogger<SelfPingService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string? _renderUrl;

        public SelfPingService(
            ILogger<SelfPingService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            
            // Render 환경변수에서 외부 URL 가져오기
            _renderUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL");
            
            if (!string.IsNullOrEmpty(_renderUrl))
            {
                _logger.LogInformation($"Self-Ping 서비스 활성화됨: {_renderUrl}");
                Console.WriteLine($"=== Self-Ping 서비스 활성화: {_renderUrl}");
            }
            else
            {
                _logger.LogInformation("로컬 환경 - Self-Ping 비활성화");
                Console.WriteLine("=== 로컬 환경 - Self-Ping 비활성화");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Render 환경이 아니면 실행하지 않음
            if (string.IsNullOrEmpty(_renderUrl))
            {
                _logger.LogInformation("Self-Ping 서비스 종료 (Render 환경 아님)");
                return;
            }

            _logger.LogInformation("Self-Ping 서비스 시작됨");
            Console.WriteLine("=== Self-Ping 서비스 시작됨");

            // 첫 번째 핑은 30초 후에 (서버 완전히 시작될 때까지 대기)
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PingSelf();
                    
                    // 13분마다 핑 (15분 타임아웃보다 짧게)
                    await Task.Delay(TimeSpan.FromMinutes(12), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // 정상 종료
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Self-Ping 오류: {ex.Message}");
                    Console.WriteLine($"=== Self-Ping 오류: {ex.Message}");
                    
                    // 오류 발생 시 1분 후 재시도
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("Self-Ping 서비스 종료됨");
            Console.WriteLine("=== Self-Ping 서비스 종료됨");
        }

        private async Task PingSelf()
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                
                // 간단한 GET 요청으로 서버를 깨움
                var response = await client.GetAsync($"{_renderUrl}/");
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Self-Ping 성공: {response.StatusCode} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"=== ✅ Self-Ping 성공: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    _logger.LogWarning($"⚠️ Self-Ping 응답 코드: {response.StatusCode}");
                    Console.WriteLine($"=== ⚠️ Self-Ping 응답 코드: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Self-Ping 실패: {ex.Message}");
                Console.WriteLine($"=== ❌ Self-Ping 실패: {ex.Message}");
            }
        }
    }
}
