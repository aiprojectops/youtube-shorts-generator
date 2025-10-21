using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace YouTubeShortsWebApp
{
    public class VideoPostProcessor
    {
        private static readonly string FFmpegPath = GetFFmpegPath();
        
        // 🔥 동시 실행 제한 (서버 안정성)
        private static readonly SemaphoreSlim _processSemaphore = new SemaphoreSlim(1, 1);

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
                return "ffmpeg";
            }

            string appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(appPath))
            {
                return appPath;
            }

            return "ffmpeg";
        }

        public static async Task<bool> IsFFmpegAvailableAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FFmpegPath,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync(cts.Token);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        // 🔥 메모리 보호를 위한 후처리 메서드
        public static async Task<string> ProcessVideoAsync(ProcessingOptions options, IProgress<string> progress = null)
        {
            // 🔥 동시 처리 제한
            await _processSemaphore.WaitAsync();
            
            var tempFiles = new List<string>();
            string currentInput = options.InputVideoPath;
        
            try
            {
                Console.WriteLine("=== 🎬 후처리 시작 (안전 모드)");
                
                // 1. 캡션만 추가 (배경음악은 스케줄에서만)
                if (!string.IsNullOrEmpty(options.CaptionText))
                {
                    string captionOutput = Path.GetTempFileName() + ".mp4";
                    tempFiles.Add(captionOutput);
                    
                    progress?.Report("캡션 추가 중...");
                    await AddCaptionAsync(currentInput, captionOutput, options);
                    
                    if (File.Exists(captionOutput))
                    {
                        // 원본 삭제 (메모리 절약)
                        if (currentInput != options.InputVideoPath && File.Exists(currentInput))
                        {
                            try 
                            { 
                                File.Delete(currentInput);
                                Console.WriteLine($"✅ 임시 파일 삭제: {Path.GetFileName(currentInput)}");
                            } 
                            catch { }
                        }
                        
                        currentInput = captionOutput;
                    }
                }
        
                // 2. 배경음악 추가 (선택적)
                if (!string.IsNullOrEmpty(options.BackgroundMusicPath) && File.Exists(options.BackgroundMusicPath))
                {
                    string musicOutput = Path.GetTempFileName() + ".mp4";
                    tempFiles.Add(musicOutput);
        
                    progress?.Report("배경음악 추가 중...");
                    await AddBackgroundMusicAsync(currentInput, musicOutput, options.BackgroundMusicPath, options.MusicVolume);
        
                    if (File.Exists(musicOutput))
                    {
                        if (File.Exists(currentInput) && currentInput != options.InputVideoPath)
                        {
                            try 
                            { 
                                File.Delete(currentInput);
                                Console.WriteLine($"✅ 임시 파일 삭제: {Path.GetFileName(currentInput)}");
                            } 
                            catch { }
                            tempFiles.Remove(currentInput);
                        }
                        
                        currentInput = musicOutput;
                    }
                    
                    // 음악 파일 삭제
                    try 
                    { 
                        File.Delete(options.BackgroundMusicPath);
                        Console.WriteLine($"✅ 음악 파일 삭제: {Path.GetFileName(options.BackgroundMusicPath)}");
                    } 
                    catch { }
                }
        
                // 3. 최종 출력
                File.Copy(currentInput, options.OutputVideoPath, true);
                Console.WriteLine($"✅ 최종 파일 생성: {Path.GetFileName(options.OutputVideoPath)}");
                
                return options.OutputVideoPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 후처리 실패: {ex.Message}");
                throw;
            }
            finally
            {
                _processSemaphore.Release();
                
                // 🔥 임시 파일 즉시 정리
                foreach (string tempFile in tempFiles)
                {
                    try
                    {
                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                            Console.WriteLine($"🧹 정리: {Path.GetFileName(tempFile)}");
                        }
                    }
                    catch { }
                }
                
                // 🔥 가비지 컬렉션
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private static async Task AddCaptionAsync(string inputPath, string outputPath, ProcessingOptions options)
        {
            try
            {
                string simpleText = options.CaptionText
                    .Replace("'", "")
                    .Replace("\"", "")
                    .Replace(":", "")
                    .Replace("\\", "");

                string actualFontSize = options.FontSize;
                string actualFontColor = options.FontColor;
                string actualPosition = options.CaptionPosition;

                if (options.FontSize == "random")
                {
                    var sizes = new[] { "60", "80", "120" };
                    actualFontSize = sizes[new Random().Next(sizes.Length)];
                }

                if (options.FontColor == "random")
                {
                    var colors = new[] { "white", "yellow", "red", "black" };
                    actualFontColor = colors[new Random().Next(colors.Length)];
                }

                if (options.CaptionPosition == "random")
                {
                    var positions = new[] { "top", "center", "bottom" };
                    actualPosition = positions[new Random().Next(positions.Length)];
                }

                string yPosition = actualPosition.ToLower() switch
                {
                    "top" => "120",
                    "center" => "h/2-text_h/2",
                    _ => "h-120"
                };

                // 🔥 간단한 FFmpeg 명령어 (빠른 처리)
                string arguments = $"-i \"{inputPath}\" " +
                                  $"-vf \"drawtext=text='{simpleText}':fontsize={actualFontSize}:fontcolor={actualFontColor}:" +
                                  $"x=(w-text_w)/2:y={yPosition}:" +
                                  $"borderw=3:bordercolor=black\" " +
                                  $"-c:a copy -preset ultrafast -y \"{outputPath}\"";

                await RunFFmpegAsync(arguments, TimeSpan.FromMinutes(2));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 캡션 추가 실패: {ex.Message}");
                throw;
            }
        }

        private static async Task AddBackgroundMusicAsync(string inputPath, string outputPath, string musicPath, float volume)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    throw new Exception($"입력 파일 없음: {inputPath}");
                }

                if (!File.Exists(musicPath))
                {
                    throw new Exception($"음악 파일 없음: {musicPath}");
                }

                int musicDuration = await GetAudioDurationAsync(musicPath);
                int randomStartTime = Math.Max(0, new Random().Next(0, Math.Max(1, musicDuration - 15)));

                // 🔥 간단한 FFmpeg 명령어
                string arguments = $"-i \"{inputPath}\" -ss {randomStartTime} -i \"{musicPath}\" " +
                                  $"-c:v copy -c:a aac " +
                                  $"-filter:a \"volume={volume:F1}\" " +
                                  $"-map 0:v:0 -map 1:a:0 " +
                                  $"-shortest -preset ultrafast -y \"{outputPath}\"";

                await RunFFmpegAsync(arguments, TimeSpan.FromMinutes(2));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 배경음악 추가 실패: {ex.Message}");
                throw;
            }
        }

        private static async Task<int> GetAudioDurationAsync(string audioPath)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffprobe",
                        Arguments = $"-v quiet -show_entries format=duration -of csv=p=0 \"{audioPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(cts.Token);

                if (process.ExitCode == 0 && double.TryParse(output.Trim(), out double duration))
                {
                    return (int)Math.Floor(duration);
                }

                return 60;
            }
            catch
            {
                return 60;
            }
        }

        public static async Task<bool> TestSimpleFFmpegAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FFmpegPath,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync(cts.Token);

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        // 🔥 타임아웃과 메모리 보호가 강화된 FFmpeg 실행
        private static async Task RunFFmpegAsync(string arguments, TimeSpan timeout)
        {
            Process process = null;
            
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                
                process = new Process
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
                
                var startTime = DateTime.Now;
                
                process.Start();
                
                // 🔥 비동기 출력 읽기 (블로킹 방지)
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync(cts.Token);
                
                var totalTime = DateTime.Now - startTime;
                
                if (process.ExitCode != 0)
                {
                    var error = await errorTask;
                    int maxLength = Math.Min(300, error.Length);
                    Console.WriteLine($"❌ FFmpeg 오류: {error.Substring(0, maxLength)}");
                    throw new Exception($"FFmpeg 실패 (코드: {process.ExitCode})");
                }
                
                Console.WriteLine($"✅ FFmpeg 완료 ({totalTime.TotalSeconds:F1}초)");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("⏱️ FFmpeg 타임아웃");
                
                if (process != null && !process.HasExited)
                {
                    try
                    {
                        process.Kill(true);
                        await Task.Delay(500);
                    }
                    catch { }
                }
                
                throw new Exception("FFmpeg 타임아웃");
            }
            finally
            {
                process?.Dispose();
            }
        }

        public static async Task<string> DownloadSampleMusicAsync()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string musicDir = Path.Combine(desktopPath, "music");
                
                if (!Directory.Exists(musicDir))
                {
                    throw new Exception($"music 폴더 없음: {musicDir}");
                }
        
                string[] supportedExtensions = { "*.mp3", "*.wav", "*.m4a", "*.aac" };
                var musicFiles = new List<string>();
        
                foreach (string extension in supportedExtensions)
                {
                    musicFiles.AddRange(Directory.GetFiles(musicDir, extension));
                }
        
                if (musicFiles.Count == 0)
                {
                    throw new Exception("music 폴더에 파일 없음");
                }
        
                Random random = new Random();
                string selectedMusic = musicFiles[random.Next(musicFiles.Count)];
        
                Console.WriteLine($"🎵 선택된 음악: {Path.GetFileName(selectedMusic)}");
        
                return selectedMusic;
            }
            catch (Exception ex)
            {
                throw new Exception($"배경음악 선택 실패: {ex.Message}");
            }
        }
    }
}
