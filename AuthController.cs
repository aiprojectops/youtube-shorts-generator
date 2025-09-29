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
            // 현재 요청의 기본 URL 가져오기
            string baseUrl = $"{Request.Scheme}://{Request.Host}";
            
            // 로깅
            Console.WriteLine($"=== OAuth 콜백 수신 ===");
            Console.WriteLine($"Base URL: {baseUrl}");
            Console.WriteLine($"Code: {(string.IsNullOrEmpty(code) ? "없음" : "있음")}");
            Console.WriteLine($"Error: {error ?? "없음"}");
            
            // 에러 체크
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"OAuth 에러: {error}");
                // 원래 페이지로 리디렉션 (새 탭이 아님)
                return Redirect($"/all-in-one?auth=error&message={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrEmpty(code))
            {
                Console.WriteLine("Authorization code가 없음");
                return Redirect("/all-in-one?auth=error&message=Authorization+code+not+received");
            }

            try
            {
                Console.WriteLine("토큰 교환 시작...");
                
                var uploader = new YouTubeUploader();
                bool success = await uploader.ExchangeCodeForTokenAsync(code, baseUrl);
                
                if (success)
                {
                    Console.WriteLine("토큰 교환 성공!");
                    // 원래 페이지로 리디렉션 (새 탭이 아님)
                    return Redirect("/all-in-one?auth=success");
                }
                else
                {
                    Console.WriteLine("토큰 교환 실패");
                    return Redirect("/all-in-one?auth=failed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"콜백 처리 오류: {ex.Message}");
                Console.WriteLine($"스택 트레이스: {ex.StackTrace}");
                return Redirect($"/all-in-one?auth=error&message={Uri.EscapeDataString(ex.Message)}");
            }
        }
    }
}
