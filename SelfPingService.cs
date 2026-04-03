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
            
            _renderUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL");
            
            if (!string.IsNullOrEmpty(_renderUrl))
            {
                _logger.LogInformation($"✅ Self-Ping 서비스 활성화: {_renderUrl}");
                Console.WriteLine($"=== ✅ Self-Ping 서비스 활성화: {_renderUrl}");
            }
            else
            {
                _logger.LogInformation("🏠 로컬 환경 - Self-Ping 비활성화");
                Console.WriteLine("=== 🏠 로컬 환경 - Self-Ping 비활성화");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrEmpty(_renderUrl))
            {
                _logger.LogInformation("Self-Ping 서비스 종료 (Render 환경 아님)");
                return;
            }

            _logger.LogInformation("🏓 Self-Ping 서비스 시작");
            Console.WriteLine("=== 🏓 Self-Ping 서비스 시작");

            // 첫 핑은 1분 후 (서버 완전 시작 대기)
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PingSelf();
                    
                    // 🔥 12분마다 핑 (15분 타임아웃보다 충분히 짧게)
                    await Task.Delay(TimeSpan.FromMinutes(12), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Self-Ping 오류: {ex.Message}");
                    Console.WriteLine($"=== ⚠️ Self-Ping 오류: {ex.Message}");
                    
                    // 오류 시 1분 후 재시도
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("🛑 Self-Ping 서비스 종료");
            Console.WriteLine("=== 🛑 Self-Ping 서비스 종료");
        }

        private async Task PingSelf()
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                
                var response = await client.GetAsync($"{_renderUrl}/");
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Self-Ping 성공: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"=== ✅ Self-Ping 성공: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    _logger.LogWarning($"⚠️ Self-Ping 응답 코드: {response.StatusCode}");
                    Console.WriteLine($"=== ⚠️ Self-Ping 응답: {response.StatusCode}");
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
