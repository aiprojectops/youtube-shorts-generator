using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;

namespace YouTubeShortsWebApp
{
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
            // 클라우드 환경에서는 시스템에 설치된 ffmpeg 사용
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER")))
            {
                Console.WriteLine("=== 클라우드 환경에서 시스템 FFmpeg 사용");
                return "ffmpeg";
            }

            Console.WriteLine($"=== BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");

            // 1. 애플리케이션 폴더에서 찾기 (로컬 환경)
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
                    string captionOutput = Path.GetTempFileName() + ".mp4";
                    tempFiles.Add(captionOutput);
                    
                    await AddCaptionAsync(currentInput, captionOutput, options);
                    
                    if (File.Exists(captionOutput))
                    {
                        currentInput = captionOutput;
                        
                        // 원본 파일 즉시 삭제 (메모리 절약)
                        if (currentInput != options.InputVideoPath && File.Exists(options.InputVideoPath))
                        {
                            try { File.Delete(options.InputVideoPath); } catch { }
                        }
                    }
                }
        
                // 2. 배경음악 추가
                if (!string.IsNullOrEmpty(options.BackgroundMusicPath) && File.Exists(options.BackgroundMusicPath))
                {
                    string musicOutput = Path.GetTempFileName() + ".mp4";
                    tempFiles.Add(musicOutput);
        
                    await AddBackgroundMusicAsync(currentInput, musicOutput, options.BackgroundMusicPath, options.MusicVolume);
        
                    if (File.Exists(musicOutput))
                    {
                        // 이전 단계 파일 즉시 삭제
                        if (File.Exists(currentInput) && currentInput != options.InputVideoPath)
                        {
                            try { File.Delete(currentInput); } catch { }
                            tempFiles.Remove(currentInput);
                        }
                        
                        currentInput = musicOutput;
                    }
                    
                    // 업로드된 음악 파일도 삭제
                    try { File.Delete(options.BackgroundMusicPath); } catch { }
                }
        
                // 3. 최종 출력
                File.Copy(currentInput, options.OutputVideoPath, true);
                
                return options.OutputVideoPath;
            }
            finally
            {
                // 임시 파일 정리는 지연 없이 즉시
                foreach (string tempFile in tempFiles)
                {
                    try
                    {
                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                        }
                    }
                    catch { } // 에러 무시하고 계속
                }
            }
        }
        

        // 캡션 추가 (개선된 버전)
        private static async Task AddCaptionAsync(string inputPath, string outputPath, ProcessingOptions options)
        {
            try
            {
                Console.WriteLine("=== 캡션 추가 시작");

                // 텍스트 간단히 처리 (특수문자 제거)
                string simpleText = options.CaptionText
                    .Replace("'", "")
                    .Replace("\"", "")
                    .Replace(":", "")
                    .Replace("\\", "");

                Console.WriteLine($"=== 처리된 텍스트: {simpleText}");
                Console.WriteLine($"=== 캡션 위치: {options.CaptionPosition}");
                Console.WriteLine($"=== 폰트 크기: {options.FontSize}");

                // 실제 값 결정 (랜덤 처리)
                string actualFontSize = options.FontSize;
                string actualFontColor = options.FontColor;
                string actualPosition = options.CaptionPosition;

                if (options.FontSize == "random")
                {
                    var sizes = new[] { "60", "80", "120" };
                    Random random = new Random();
                    actualFontSize = sizes[random.Next(sizes.Length)];
                    Console.WriteLine($"=== 랜덤 선택된 크기: {actualFontSize}");
                }

                if (options.FontColor == "random")
                {
                    var colors = new[] { "white", "yellow", "red", "black" };
                    Random random = new Random();
                    actualFontColor = colors[random.Next(colors.Length)];
                    Console.WriteLine($"=== 랜덤 선택된 색상: {actualFontColor}");
                }

                if (options.CaptionPosition == "random")
                {
                    var positions = new[] { "top", "center", "bottom" };
                    Random random = new Random();
                    actualPosition = positions[random.Next(positions.Length)];
                    Console.WriteLine($"=== 랜덤 선택된 위치: {actualPosition}");
                }

                // 위치별 Y 좌표 계산
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

                Console.WriteLine($"=== Y 위치 계산: {yPosition}");
                Console.WriteLine($"=== 최종 설정 - 위치: {actualPosition}, 크기: {actualFontSize}, 색상: {actualFontColor}");

                // 개선된 FFmpeg 명령어 (실제 값 사용)
                string arguments = $"-i \"{inputPath}\" " +
                                  $"-vf \"drawtext=text='{simpleText}':fontsize={actualFontSize}:fontcolor={actualFontColor}:" +
                                  $"x=(w-text_w)/2:y={yPosition}:" +
                                  $"borderw=3:bordercolor=black:shadowx=2:shadowy=2:shadowcolor=black@0.5\" " +
                                  $"-c:a copy -preset ultrafast -crf 23 -y \"{outputPath}\"";

                Console.WriteLine($"=== FFmpeg 명령어: {arguments}");

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

                // 파일 존재 확인
                if (!File.Exists(inputPath))
                {
                    throw new Exception($"입력 비디오 파일이 없습니다: {inputPath}");
                }

                if (!File.Exists(musicPath))
                {
                    throw new Exception($"배경음악 파일이 없습니다: {musicPath}");
                }

                Console.WriteLine($"=== 입력 파일 확인 완료");

                // 음악 파일의 길이 확인
                int musicDuration = await GetAudioDurationAsync(musicPath);
                Console.WriteLine($"=== 음악 파일 길이: {musicDuration}초");

                // 랜덤 시작점 계산 (음악 길이의 70% 범위 내에서)
                Random random = new Random();
                int maxStartTime = Math.Max(0, musicDuration - 15); // 최소 15초는 남겨두기
                int randomStartTime = random.Next(0, Math.Max(1, maxStartTime));

                Console.WriteLine($"=== 랜덤 시작점: {randomStartTime}초");

                // 랜덤 시작점을 적용한 FFmpeg 명령어
                string arguments = $"-i \"{inputPath}\" -ss {randomStartTime} -i \"{musicPath}\" " +
                                  $"-c:v copy -c:a aac " +
                                  $"-filter:a \"volume={volume:F1}\" " +
                                  $"-map 0:v:0 -map 1:a:0 " +
                                  $"-shortest -preset ultrafast -y \"{outputPath}\"";

                Console.WriteLine($"=== FFmpeg 명령어 (랜덤 시작): {arguments}");

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

                return 60; // 기본값
            }
            catch
            {
                return 60; // 기본값
            }
        }

        // FFmpeg 간단 테스트 (설정 페이지에서 사용)
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

        private static async Task RunFFmpegAsync(string arguments)
        {
            Process process = null;
            
            try
            {
                Console.WriteLine($"=== 안전한 FFmpeg 실행 시작");
                
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                
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
                Console.WriteLine("=== FFmpeg 프로세스 시작됨");
                
                // 비동기 출력 읽기
                var outputTask = Task.Run(async () => await process.StandardOutput.ReadToEndAsync());
                var errorTask = Task.Run(async () => await process.StandardError.ReadToEndAsync());
                
                // 프로세스 완료 대기
                await process.WaitForExitAsync(cts.Token);
                
                var output = await outputTask;
                var error = await errorTask;
                
                var totalTime = DateTime.Now - startTime;
                Console.WriteLine($"=== FFmpeg 완료 (총 {totalTime.TotalSeconds:F1}초)");
                
                if (process.ExitCode != 0)
                {
                    // 🔥 수정된 부분
                    if (!string.IsNullOrEmpty(error))
                    {
                        int maxLength = Math.Min(500, error.Length);
                        Console.WriteLine($"=== FFmpeg 에러: {error.Substring(0, maxLength)}");
                    }
                    throw new Exception($"FFmpeg 오류 (코드: {process.ExitCode})");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("=== FFmpeg 타임아웃, 강제 종료 시도");
                
                if (process != null && !process.HasExited)
                {
                    try
                    {
                        process.Kill(true);
                        await Task.Delay(1000);
                    }
                    catch (Exception killEx)
                    {
                        Console.WriteLine($"=== 프로세스 종료 실패: {killEx.Message}");
                    }
                }
                
                throw new Exception("FFmpeg 타임아웃으로 종료됨");
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
                Console.WriteLine("=== 배경음악 파일 선택 시작");
        
                // 사용자 바탕화면의 music 폴더 사용
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string musicDir = Path.Combine(desktopPath, "music");
                
                Console.WriteLine($"=== 바탕화면 경로: {desktopPath}");
                Console.WriteLine($"=== 음악 폴더 경로: {musicDir}");
                Console.WriteLine($"=== 음악 폴더 존재: {Directory.Exists(musicDir)}");
        
                if (!Directory.Exists(musicDir))
                {
                    throw new Exception($"바탕화면에 music 폴더가 없습니다: {musicDir}");
                }
        
                string[] supportedExtensions = { "*.mp3", "*.wav", "*.m4a", "*.aac" };
                var musicFiles = new List<string>();
        
                foreach (string extension in supportedExtensions)
                {
                    var files = Directory.GetFiles(musicDir, extension);
                    Console.WriteLine($"=== {extension} 파일 {files.Length}개 발견");
                    musicFiles.AddRange(files);
                }
        
                Console.WriteLine($"=== 총 음악 파일 개수: {musicFiles.Count}");
        
                if (musicFiles.Count == 0)
                {
                    // 실제 파일들 확인
                    var allFiles = Directory.GetFiles(musicDir);
                    Console.WriteLine($"=== 음악 폴더의 모든 파일 ({allFiles.Length}개):");
                    foreach (var file in allFiles.Take(10))
                    {
                        Console.WriteLine($"    - {Path.GetFileName(file)}");
                    }
        
                    throw new Exception($"music 폴더에 음악 파일이 없습니다. 지원 형식: mp3, wav, m4a, aac");
                }
        
                Random random = new Random();
                string selectedMusic = musicFiles[random.Next(musicFiles.Count)];
        
                Console.WriteLine($"=== 선택된 배경음악: {Path.GetFileName(selectedMusic)}");
                Console.WriteLine($"=== 선택된 파일 존재: {File.Exists(selectedMusic)}");
        
                // 파일 크기 확인
                if (File.Exists(selectedMusic))
                {
                    var fileInfo = new FileInfo(selectedMusic);
                    Console.WriteLine($"=== 음악 파일 크기: {fileInfo.Length / 1024} KB");
                }
        
                return selectedMusic;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== 배경음악 선택 실패: {ex.Message}");
                throw new Exception($"배경음악 파일을 찾을 수 없습니다: {ex.Message}");
            }
        }
    }
}
