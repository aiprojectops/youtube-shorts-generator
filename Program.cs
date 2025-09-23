using YouTubeShortsWebApp;
using YouTubeShortsWebApp.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ScheduledUploadService 등록
builder.Services.AddSingleton<ScheduledUploadService>();
builder.Services.AddHostedService<ScheduledUploadService>(provider =>
    provider.GetRequiredService<ScheduledUploadService>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// YouTube OAuth 콜백 라우트 추가
app.MapGet("/auth/google/callback", async (HttpContext context) =>
{
    var code = context.Request.Query["code"].ToString();
    var error = context.Request.Query["error"].ToString();

    // 현재 요청의 기본 URL 가져오기 (Render에서 동적으로 처리)
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

    if (!string.IsNullOrEmpty(error))
    {
        return Results.Redirect($"{baseUrl}/?auth=error&message=" + Uri.EscapeDataString(error));
    }

    if (string.IsNullOrEmpty(code))
    {
        return Results.Redirect($"{baseUrl}/?auth=error&message=" + Uri.EscapeDataString("인증 코드를 받지 못했습니다."));
    }

    try
    {
        var uploader = new YouTubeUploader();
        bool success = await uploader.ExchangeCodeForTokenAsync(code, baseUrl);
        
        if (success)
        {
            return Results.Redirect($"{baseUrl}/?auth=success");
        }
        else
        {
            return Results.Redirect($"{baseUrl}/?auth=error&message=" + Uri.EscapeDataString("토큰 교환에 실패했습니다."));
        }
    }
    catch (Exception ex)
    {
        return Results.Redirect($"{baseUrl}/?auth=error&message=" + Uri.EscapeDataString(ex.Message));
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
