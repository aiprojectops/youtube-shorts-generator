using Microsoft.AspNetCore.Mvc;

namespace YouTubeShortsWebApp
{
    [Route("oauth")]
    public class AuthController : ControllerBase
    {
        [HttpGet("google/callback")]
        public async Task<IActionResult> GoogleCallback([FromQuery] string code, [FromQuery] string? state = null, [FromQuery] string? error = null)
        {
            // 에러 체크 추가
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"OAuth 에러: {error}");
                return Redirect($"/youtube-upload?auth=error&message={Uri.EscapeDataString(error)}");
            }
    
            if (string.IsNullOrEmpty(code))
            {
                Console.WriteLine("Authorization code가 없음");
                return Redirect("/youtube-upload?auth=error&message=Authorization+code+not+received");
            }
    
            try
            {
                Console.WriteLine($"받은 code: {code}");
                
                var uploader = new YouTubeUploader();
                string baseUrl = $"{Request.Scheme}://{Request.Host}";
                
                Console.WriteLine($"Base URL: {baseUrl}");
                
                bool success = await uploader.ExchangeCodeForTokenAsync(code, baseUrl);
                
                if (success)
                {
                    Console.WriteLine("토큰 교환 성공");
                    return Redirect("/youtube-upload?auth=success");
                }
                else
                {
                    Console.WriteLine("토큰 교환 실패");
                    return Redirect("/youtube-upload?auth=failed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"콜백 처리 오류: {ex.Message}");
                return Redirect($"/youtube-upload?auth=error&message={Uri.EscapeDataString(ex.Message)}");
            }
        }
    }
}
