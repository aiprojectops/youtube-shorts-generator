<artifact identifier="program-cs-updated" type="application/vnd.ant.code" language="csharp" title="Program.cs">
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
// 🔥 공통 서비스 등록
builder.Services.AddScoped<VideoGenerationService>();
builder.Services.AddScoped<YouTubeUploadService>();
// ScheduledUploadService 등록
builder.Services.AddSingleton<ScheduledUploadService>();
builder.Services.AddHostedService<ScheduledUploadService>(provider =>
provider.GetRequiredService<ScheduledUploadService>());
// Self-Ping 서비스 추가
builder.Services.AddHostedService<SelfPingService>();
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
app.MapControllers();
app.MapRazorComponents<App>()
.AddInteractiveServerRenderMode();
app.Run();
</artifact>
