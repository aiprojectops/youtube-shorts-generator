using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;

namespace YouTubeShortsWebApp
{
    /// <summary>
    /// 🚀 512MB RAM 최적화 버전
    /// FFmpeg 메모리 사용량 최소화
    /// </summary>
    public class VideoPostProcessor
    {
        private static readonly string FFmpegPath = GetFFmpegPath();

        public class ProcessingOptions
        {
            public string InputVideoPath { get; set; } = "";
            public string OutputVideoPath { get; set; } = "";
            public string CaptionText { get; set; } = "";
            public string FontSize { get; set; } = "48";
            public string FontColor { get; set; } = "white";
            public string CaptionPosition { get; set; } = "bottom";
            public string BackgroundMusicPath { get; set; } = "";
            public float MusicVolume { get; set; } = 0.3f;
        }

        private static string GetFFmpegPath()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER")))
            {
                Console.WriteLine("=== 클라우드 환경에서 시스템 FFmpeg 사용");
                return "ffmpeg";
            }

            Console.WriteLine($"=== BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");
            string appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            Console.WriteLine($"=== 찾는 경로: {appPath}");
            Console.WriteLine($"=== 파일 존재: {File.Exists(appPath)}");

            if (File.Exists(appPath))
            {
                Console.WriteLine($"=== FFmpeg 찾음: {appPath}");
                return appPath;
            }

            Console.WriteLine("=== 애플리케이션 폴더에서 찾지 못함, PATH 사용");
            return "ffmpeg";
        }

        public static async Task<bool> IsFFmpegAvailableAsync()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FFmpegPath,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> ProcessVideoAsync(ProcessingOptions options, IProgress<string> progress = null)
        {
            var tempFiles = new List<string>();
            string currentInput = options.InputVideoPath;

            try
            {
                // 1. 캡션 추가
                if (!string.IsNullOrEmpty(options.CaptionText))
                {
                    progress?.Report("캡션 추가 중...");
                    string captionOutput = Path.Combine(Path.GetTempPath(), $"caption_{Guid.NewGuid()}.mp4");
                    tempFiles.Add(captionOutput);
                    await AddCaptionAsync(currentInput, captionOutput, options);
                    currentInput = captionOutput;
                    
                    // 🧹 메모리 정리
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                // 2. 배경음악 추가
                if (!string.IsNullOrEmpty(options.BackgroundMusicPath) && File.Exists(options.BackgroundMusicPath))
                {
                    progress?.Report("배경음악 추가 중...");
                    await AddBackgroundMusicAsync(currentInput, options.OutputVideoPath, options.BackgroundMusicPath, options.MusicVolume);
                    
                    // 🧹 메모리 정리
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                else
                {
                    if (currentInput != options.OutputVideoPath)
                    {
                        File.Copy(currentInput, options.OutputVideoPath, true);
                    }
                }

                progress?.Report("완료!");
                return options.OutputVideoPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== 영상 처리 실패: {ex.Message}");
                throw;
            }
            finally
            {
                foreach (var tempFile in tempFiles)
                {
                    try
                    {
                        if (File.Exists(tempFile) && tempFile != options.OutputVideoPath)
                        {
                            File.Delete(tempFile);
                        }
                    }
                    catch { }
                }
            }
        }

        private static async Task AddCaptionAsync(string inputPath, string outputPath, ProcessingOptions options)
        {
            try
            {
                Console.WriteLine("=== 캡션 추가 시작");
                
                string simpleText = options.CaptionText
                    .Replace("'", "\\'")
                    .Replace("\"", "")
                    .Replace("\n", " ")
                    .Replace("\r", "");

                Console.WriteLine($"=== 처리된 텍스트: {simpleText}");

                Random random = new Random();
                string actualPosition = options.CaptionPosition;
                string actualFontSize = options.FontSize;
                string actualFontColor = options.FontColor;

                // 🔥 랜덤 위치 처리
                if (options.CaptionPosition == "random")
                {
                    var positions = new[] { "top", "center", "bottom" };
                    actualPosition = positions[random.Next(positions.Length)];
                    Console.WriteLine($"=== 랜덤 선택된 위치: {actualPosition}");
                }

                // 🔥 랜덤 크기 처리
                if (options.FontSize == "random")
                {
                    var sizes = new[] { "60", "80", "120" };
                    actualFontSize = sizes[random.Next(sizes.Length)];
                    Console.WriteLine($"=== 랜덤 선택된 크기: {actualFontSize}");
                }

                // 🔥 랜덤 색상 처리
                if (options.FontColor == "random")
                {
                    var colors = new[] { "white", "yellow", "red", "black" };
                    actualFontColor = colors[random.Next(colors.Length)];
                    Console.WriteLine($"=== 랜덤 선택된 색상: {actualFontColor}");
                }

                string yPosition;
                switch (actualPosition.ToLower())
                {
                    case "top":
                        yPosition = "120";
                        break;
                    case "center":
                        yPosition = "h/2-text_h/2";
                        break;
                    case "bottom":
                    default:
                        yPosition = "h-120";
                        break;
                }

                Console.WriteLine($"=== 최종 설정 - 위치: {actualPosition}, 크기: {actualFontSize}, 색상: {actualFontColor}");

                // 🚀 512MB 최적화 FFmpeg 명령어
                string arguments = $"-i \"{inputPath}\" " +
                                  $"-vf \"drawtext=text='{simpleText}':fontsize={actualFontSize}:fontcolor={actualFontColor}:" +
                                  $"x=(w-text_w)/2:y={yPosition}:" +
                                  $"borderw=3:bordercolor=black:shadowx=2:shadowy=2:shadowcolor=black@0.5\" " +
                                  $"-c:a copy " +
                                  $"-threads 1 " +              // 🔥 단일 스레드 (메모리 ↓)
                                  $"-preset ultrafast " +       // 빠른 처리
                                  $"-crf 28 " +                 // 🔥 높은 압축률 (메모리 ↓)
                                  $"-max_muxing_queue_size 512 " + // 🔥 버퍼 제한 (메모리 ↓)
                                  $"-y \"{outputPath}\"";

                Console.WriteLine($"=== FFmpeg 명령어 (512MB 최적화): {arguments}");

                await RunFFmpegAsync(arguments);

                Console.WriteLine("=== 캡션 추가 완료");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== 캡션 추가 오류: {ex.Message}");
                throw;
            }
        }

        private static async Task AddBackgroundMusicAsync(string inputPath, string outputPath, string musicPath, float volume)
        {
            try
            {
                Console.WriteLine("=== 배경음악 추가 시작 (랜덤 시작점 포함)");
                Console.WriteLine($"=== 입력 비디오: {inputPath}");
                Console.WriteLine($"=== 배경음악: {musicPath}");
                Console.WriteLine($"=== 출력: {outputPath}");
                Console.WriteLine($"=== 음량: {volume}");

                if (!File.Exists(inputPath))
                {
                    throw new Exception($"입력 비디오 파일이 없습니다: {inputPath}");
                }

                if (!File.Exists(musicPath))
                {
                    throw new Exception($"배경음악 파일이 없습니다: {musicPath}");
                }

                Console.WriteLine($"=== 입력 파일 확인 완료");

                int musicDuration = await GetAudioDurationAsync(musicPath);
                Console.WriteLine($"=== 음악 파일 길이: {musicDuration}초");

                Random random = new Random();
                int maxStartTime = Math.Max(0, musicDuration - 15);
                int randomStartTime = random.Next(0, Math.Max(1, maxStartTime));

                Console.WriteLine($"=== 랜덤 시작점: {randomStartTime}초");

                // 🚀 512MB 최적화 FFmpeg 명령어
                string arguments = $"-i \"{inputPath}\" -ss {randomStartTime} -i \"{musicPath}\" " +
                                  $"-c:v copy -c:a aac " +
                                  $"-filter:a \"volume={volume:F1}\" " +
                                  $"-map 0:v:0 -map 1:a:0 " +
                                  $"-shortest " +
                                  $"-threads 1 " +              // 🔥 단일 스레드 (메모리 ↓)
                                  $"-preset ultrafast " +       // 빠른 처리
                                  $"-max_muxing_queue_size 512 " + // 🔥 버퍼 제한 (메모리 ↓)
                                  $"-y \"{outputPath}\"";

                Console.WriteLine($"=== FFmpeg 명령어 (512MB 최적화): {arguments}");

                await RunFFmpegAsync(arguments);

                Console.WriteLine("=== 배경음악 추가 완료 (랜덤 시작점 적용됨)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== 배경음악 추가 오류: {ex.Message}");
                throw;
            }
        }

        private static async Task<int> GetAudioDurationAsync(string audioPath)
        {
            try
            {
                string arguments = $"-v quiet -show_entries format=duration -of csv=p=0 \"{audioPath}\"";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffprobe",
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    if (double.TryParse(output.Trim(), out double duration))
                    {
                        return (int)Math.Floor(duration);
                    }
                }

                return 60;
            }
            catch
            {
                return 60;
            }
        }

        private static async Task RunFFmpegAsync(string arguments)
        {
            try
            {
                Console.WriteLine($"🎬 FFmpeg 처리 중 (512MB 최적화 모드)...");

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FFmpegPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetTempPath()
                    }
                };

                process.Start();
                var startTime = DateTime.Now;
                var processTask = process.WaitForExitAsync();

                var maxTimeout = TimeSpan.FromMinutes(10);

                while (!processTask.IsCompleted)
                {
                    var elapsed = DateTime.Now - startTime;

                    if (elapsed >= maxTimeout)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                            }
                        }
                        catch { }
                        throw new TimeoutException("FFmpeg 실행이 10분을 초과했습니다.");
                    }

                    await Task.Delay(1000);
                }

                if (!process.HasExited)
                {
                    process.WaitForExit();
                }

                if (process.ExitCode != 0)
                {
                    string errorOutput = await process.StandardError.ReadToEndAsync();
                    throw new Exception($"FFmpeg 실패 (종료코드 {process.ExitCode}): {errorOutput}");
                }

                Console.WriteLine("=== FFmpeg 처리 완료 ✅");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== FFmpeg 실행 오류: {ex.Message}");
                throw;
            }
        }

        public static async Task<bool> TestSimpleFFmpegAsync()
        {
            try
            {
                Console.WriteLine("=== FFmpeg 간단 테스트 시작");

                string arguments = "-version";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FFmpegPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(1));
                var processTask = process.WaitForExitAsync();
                var completedTask = await Task.WhenAny(processTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    process.Kill();
                    Console.WriteLine("=== FFmpeg 버전 확인 타임아웃");
                    return false;
                }

                string output = await process.StandardOutput.ReadToEndAsync();
                Console.WriteLine($"=== FFmpeg 버전 정보: {output.Substring(0, Math.Min(200, output.Length))}");

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== FFmpeg 테스트 오류: {ex.Message}");
                return false;
            }
        }

        public static async Task<string> DownloadSampleMusicAsync()
        {
            try
            {
                string musicDir = Path.Combine(Path.GetTempPath(), "SampleMusic");
                Directory.CreateDirectory(musicDir);

                string musicPath = Path.Combine(musicDir, "sample_music.mp3");

                if (File.Exists(musicPath))
                {
                    return musicPath;
                }

                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);

                string musicUrl = "https://www.soundhelix.com/examples/mp3/SoundHelix-Song-1.mp3";

                Console.WriteLine($"=== 샘플 음악 다운로드 중: {musicUrl}");

                // 🚀 스트리밍 다운로드 (메모리 최적화)
                using (var response = await httpClient.GetAsync(musicUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(musicPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
                    {
                        await contentStream.CopyToAsync(fileStream, 81920);
                    }
                }

                Console.WriteLine($"=== 샘플 음악 다운로드 완료: {musicPath}");
                return musicPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== 샘플 음악 다운로드 실패: {ex.Message}");
                return null;
            }
        }
    }
}
