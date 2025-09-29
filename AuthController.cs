using Microsoft.AspNetCore.Mvc;

namespace YouTubeShortsWebApp
{
    [Route("oauth")]
    public class AuthController : ControllerBase
    {
        [HttpGet("google/callback")]
        public async Task<IActionResult> GoogleCallback(
            [FromQuery] string code, 
            [FromQuery] string? state = null, 
            [FromQuery] string? error = null)
        {
            string baseUrl = $"{Request.Scheme}://{Request.Host}";
            
            // state에서 돌아갈 페이지 확인 (기본값: youtube-upload)
            string returnPage = state ?? "youtube-upload";
            
            Console.WriteLine($"=== OAuth 콜백 수신 ===");
            Console.WriteLine($"Base URL: {baseUrl}");
            Console.WriteLine($"Return Page: {returnPage}");
            Console.WriteLine($"Code: {(string.IsNullOrEmpty(code) ? "없음" : "있음")}");
            Console.WriteLine($"Error: {error ?? "없음"}");
            
            // 에러 체크
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"OAuth 에러: {error}");
                return Redirect($"/{returnPage}?auth=error&message={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrEmpty(code))
            {
                Console.WriteLine("Authorization code가 없음");
                return Redirect($"/{returnPage}?auth=error&message=Authorization+code+not+received");
            }

            try
            {
                Console.WriteLine("토큰 교환 시작...");
                
                var uploader = new YouTubeUploader();
                bool success = await uploader.ExchangeCodeForTokenAsync(code, baseUrl);
                
                if (success)
                {
                    Console.WriteLine("토큰 교환 성공!");
                    return Redirect($"/{returnPage}?auth=success");
                }
                else
                {
                    Console.WriteLine("토큰 교환 실패");
                    return Redirect($"/{returnPage}?auth=failed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"콜백 처리 오류: {ex.Message}");
                Console.WriteLine($"스택 트레이스: {ex.StackTrace}");
                return Redirect($"/{returnPage}?auth=error&message={Uri.EscapeDataString(ex.Message)}");
            }
        }
    }
}
