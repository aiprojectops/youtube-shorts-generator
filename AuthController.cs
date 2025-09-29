using Microsoft.AspNetCore.Mvc;

namespace YouTubeShortsWebApp
{
    [Route("oauth")]
    public class AuthController : ControllerBase
    {
        // AuthController.cs - 수정 필요
        [HttpGet("google/callback")]
        public async Task<IActionResult> GoogleCallback(
            [FromQuery] string code, 
            [FromQuery] string? state = null, 
            [FromQuery] string? error = null)
        {
            string baseUrl = $"{Request.Scheme}://{Request.Host}";
            
            // state 파라미터로 돌아갈 페이지 결정 (선택사항)
            string returnPage = state ?? "youtube-upload"; // 기본은 youtube-upload
            
            if (!string.IsNullOrEmpty(error))
            {
                return Redirect($"/{returnPage}?auth=error&message={Uri.EscapeDataString(error)}");
            }
        
            if (string.IsNullOrEmpty(code))
            {
                return Redirect($"/{returnPage}?auth=error&message=Authorization+code+not+received");
            }
        
            try
            {
                var uploader = new YouTubeUploader();
                bool success = await uploader.ExchangeCodeForTokenAsync(code, baseUrl);
                
                if (success)
                {
                    return Redirect($"/{returnPage}?auth=success");
                }
                else
                {
                    return Redirect($"/{returnPage}?auth=failed");
                }
            }
            catch (Exception ex)
            {
                return Redirect($"/{returnPage}?auth=error&message={Uri.EscapeDataString(ex.Message)}");
            }
        }
    }
}
