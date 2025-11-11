using Microsoft.AspNetCore.Components.Forms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YouTubeShortsWebApp.Services
{
    /// <summary>
    /// 영상 생성 + 후처리 통합 서비스
    /// VideoGenerator, AllInOne에서 공통으로 사용
    /// </summary>
    public class VideoPostProcessingService
    {
        private readonly VideoGenerationService _videoGenService;
        private readonly UserSettingsService _userSettings;

        public VideoPostProcessingService(VideoGenerationService videoGenService, UserSettingsService userSettings)
        {
            _videoGenService = videoGenService;
            _userSettings = userSettings;
        }

        /// <summary>
        /// 영상 처리 옵션 (생성 + 후처리)
        /// </summary>
        public class ProcessingOptions
        {
            public VideoGenerationService.VideoGenerationOptions GenerationOptions { get; set; }
            public VideoGenerationService.PostProcessingOptions PostProcessingOptions { get; set; }
        }

        /// <summary>
        /// 영상 처리 결과
        /// </summary>
        public class ProcessingResult
        {
            public bool Success { get; set; }
            public string FilePath { get; set; }
            public string FileName { get; set; }
            public string Prompt { get; set; }
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// 진행 상황 콜백
        /// (현재 번호, 전체 개수, 상태 메시지)
        /// </summary>
        public delegate void ProgressCallback(int current, int total, string status);

        /// <summary>
        /// 영상 생성 + 후처리 일괄 실행
        /// </summary>
        public async Task<List<ProcessingResult>> ProcessVideosAsync(
            ProcessingOptions options,
            ProgressCallback progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ProcessingResult>();

            // 총 처리할 영상 개수
            int totalVideos = options.GenerationOptions.IsGenerateVideo
                ? options.GenerationOptions.VideoCount
                : options.GenerationOptions.LocalVideoFiles.Count;

            Console.WriteLine($"🎬 영상 처리 시작: 총 {totalVideos}개");

            for (int i = 0; i < totalVideos; i++)
            {
                // 취소 체크
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("⚠️ 사용자에 의해 중단됨");
                    break;
                }

                int currentIndex = i + 1;
                string status = $"영상 {currentIndex}/{totalVideos} 처리 중...";
                
                try
                {
                    progressCallback?.Invoke(currentIndex, totalVideos, status);

                    // VideoGenerationService 호출
                    var genResult = await _videoGenService.GenerateOrProcessVideoAsync(
                        currentIndex,
                        options.GenerationOptions,
                        options.PostProcessingOptions,
                        (s) => progressCallback?.Invoke(currentIndex, totalVideos, s)
                    );

                    if (genResult.Success)
                    {
                        results.Add(new ProcessingResult
                        {
                            Success = true,
                            FilePath = genResult.VideoPath,
                            FileName = genResult.FileName,
                            Prompt = genResult.Prompt
                        });

                        Console.WriteLine($"✅ 영상 {currentIndex} 완료: {genResult.FileName}");
                    }
                    else
                    {
                        results.Add(new ProcessingResult
                        {
                            Success = false,
                            ErrorMessage = genResult.ErrorMessage
                        });

                        Console.WriteLine($"❌ 영상 {currentIndex} 실패: {genResult.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 영상 {currentIndex} 처리 중 오류: {ex.Message}");
                    
                    results.Add(new ProcessingResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            Console.WriteLine($"🎬 영상 처리 완료: {results.Count(r => r.Success)}/{totalVideos} 성공");

            return results;
        }

        /// <summary>
        /// 단일 영상 처리
        /// </summary>
        public async Task<ProcessingResult> ProcessSingleVideoAsync(
            ProcessingOptions options,
            Action<string> statusCallback = null)
        {
            try
            {
                var genResult = await _videoGenService.GenerateOrProcessVideoAsync(
                    1,
                    options.GenerationOptions,
                    options.PostProcessingOptions,
                    statusCallback
                );

                return new ProcessingResult
                {
                    Success = genResult.Success,
                    FilePath = genResult.VideoPath,
                    FileName = genResult.FileName,
                    Prompt = genResult.Prompt,
                    ErrorMessage = genResult.ErrorMessage
                };
            }
            catch (Exception ex)
            {
                return new ProcessingResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 처리 가능 여부 검증
        /// </summary>
        public (bool IsValid, string ErrorMessage) ValidateOptions(ProcessingOptions options)
        {
            if (options == null)
                return (false, "옵션이 null입니다.");
        
            if (options.GenerationOptions == null)
                return (false, "생성 옵션이 null입니다.");

            // AI 생성 모드 검증
            if (options.GenerationOptions.IsGenerateVideo)
            {
                // 🆕 사용자별 API 키 확인
                if (string.IsNullOrEmpty(_userSettings.GetReplicateApiKey()))
                    return (false, "Replicate API 키가 설정되지 않았습니다.");
            
                // 🆕 프롬프트 검증 (CSV 또는 직접입력)
                if (options.GenerationOptions.UseDirectPrompt)
                {
                    // 직접 입력 모드
                    if (string.IsNullOrWhiteSpace(options.GenerationOptions.DirectPrompt))
                        return (false, "프롬프트를 입력해주세요.");
                }
                else
                {
                    // CSV 모드
                    if (options.GenerationOptions.CsvPrompts == null || options.GenerationOptions.CsvPrompts.Count == 0)
                        return (false, "CSV 프롬프트가 로드되지 않았습니다.");
                }
            
                if (options.GenerationOptions.VideoCount < 1)
                    return (false, "생성할 영상 개수는 1개 이상이어야 합니다.");
            }
            // 로컬 파일 모드 검증
            else
            {
                if (options.GenerationOptions.LocalVideoFiles == null || options.GenerationOptions.LocalVideoFiles.Count == 0)
                    return (false, "로컬 비디오 파일이 선택되지 않았습니다.");
            }

            // 후처리 검증
            if (options.PostProcessingOptions?.EnablePostProcessing == true)
            {
                if (options.PostProcessingOptions.AddCaption && 
                    !options.PostProcessingOptions.UseRandomCaption &&
                    string.IsNullOrWhiteSpace(options.PostProcessingOptions.CaptionText))
                {
                    return (false, "캡션 텍스트가 입력되지 않았습니다.");
                }

                if (options.PostProcessingOptions.AddBackgroundMusic &&
                    (options.PostProcessingOptions.SelectedMusicFiles == null || 
                     options.PostProcessingOptions.SelectedMusicFiles.Count == 0))
                {
                    return (false, "배경 음악 파일이 선택되지 않았습니다.");
                }
            }

            return (true, "");
        }
    }
}
