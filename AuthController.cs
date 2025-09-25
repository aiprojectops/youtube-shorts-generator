using Microsoft.AspNetCore.Mvc;

namespace YouTubeShortsWebApp
{
    [Route("oauth")]  // "auth" -> "oauth"로 변경해서 충돌 방지
    public class AuthController : ControllerBase
    {
        [HttpGet("google/callback")]
        public async Task<IActionResult> GoogleCallback([FromQuery] string code, [FromQuery] string? state = null)
        {
            if (string.IsNullOrEmpty(code))
            {
                return BadRequest("Authorization code not received");
            }

            try
            {
                var uploader = new YouTubeUploader();
                string baseUrl = $"{Request.Scheme}://{Request.Host}";
                
                bool success = await uploader.ExchangeCodeForTokenAsync(code, baseUrl);
                
                if (success)
                {
                    return Redirect("/youtube-upload?auth=success");
                }
                else
                {
                    return Redirect("/youtube-upload?auth=failed");
                }
            }
            catch (Exception ex)
            {
                return Redirect($"/youtube-upload?auth=error&message={Uri.EscapeDataString(ex.Message)}");
            }
        }
    }
}
