using YouTubeShortsWebApp;
using YouTubeShortsWebApp.Components;
using YouTubeShortsWebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// HttpClientFactory 추가 (Self-Ping용)
builder.Services.AddHttpClient();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

// 🔥 기존 서비스 등록
builder.Services.AddScoped<VideoGenerationService>();
builder.Services.AddScoped<YouTubeUploadService>();
// 🔥 새로운 통합 서비스 등록
builder.Services.AddScoped<VideoPostProcessingService>();
builder.Services.AddScoped<UploadScheduleService>();
builder.Services.AddScoped<UserSettingsService>();

// 🔥 전역 공유 데이터 (Singleton)
builder.Services.AddSingleton<SharedMemoryDataStore>();

// 🔥 백그라운드 작업 (Singleton이지만 유저별 분리 필요)
builder.Services.AddSingleton<ScheduledUploadService>();
builder.Services.AddHostedService<ScheduledUploadService>(provider =>
    provider.GetRequiredService<ScheduledUploadService>());

// Self-Ping 서비스 추가
builder.Services.AddHostedService<SelfPingService>();

// 🆕 세션 활성화 (유저 식별용)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();  // HTTP → HTTPS 리다이렉션
app.UseStaticFiles();  // CSS, JS 등 정적 파일 제공
app.UseAntiforgery();   // CSRF 공격 방어

// 🆕 세션 미들웨어 추가
app.UseSession();

app.MapControllers();   // API 엔드포인트 매핑
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();  // Blazor Server 모드

app.Run();
