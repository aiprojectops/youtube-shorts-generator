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
        
            // 🔥 쿠키에서 UserId 읽기 (최우선)
            string userIdFromCookie = Request.Cookies["userId"];
            
            // state에서 returnPage와 userId 분리
            string returnPage = "youtube-upload";
            string userId = null;
            
            if (!string.IsNullOrEmpty(state))
            {
                var parts = state.Split('|');
                returnPage = parts[0];
                if (parts.Length > 1)
                {
                    userId = parts[1];
                }
            }
            
            // 🔥 쿠키 우선, 없으면 state 사용
            userId = userIdFromCookie ?? userId;
            
            Console.WriteLine($"=== OAuth 콜백 수신 ===");
            Console.WriteLine($"UserId from Cookie: {userIdFromCookie ?? "없음"}");
            Console.WriteLine($"UserId from State: {userId ?? "없음"}");
            Console.WriteLine($"최종 UserId: {userId ?? "없음"}");
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
        
                var uploader = new YouTubeUploader(userId);
                bool success = await uploader.ExchangeCodeForTokenAsync(code, baseUrl);
                
                if (success)
                {
                    Console.WriteLine("토큰 교환 성공!");
                    return Redirect($"/{returnPage}?auth=success&userId={Uri.EscapeDataString(userId ?? "")}");
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
