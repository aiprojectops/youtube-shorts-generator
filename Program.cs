using YouTubeShortsWebApp;
using YouTubeShortsWebApp.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddControllers(); // 컨트롤러 서비스 추가

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

app.MapControllers(); // 컨트롤러 라우팅 추가 - 이게 AuthController를 처리함

// 기존의 MapGet 콜백 제거 - AuthController가 대신 처리함

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
