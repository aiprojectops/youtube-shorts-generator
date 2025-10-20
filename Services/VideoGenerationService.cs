using Microsoft.AspNetCore.Components.Forms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace YouTubeShortsWebApp.Services
{
/// <summary>
/// 영상 생성 관련 공통 로직을 처리하는 서비스
/// </summary>
public class VideoGenerationService
{
private readonly Random _random = new Random();
  public class VideoGenerationOptions
  {
      public bool IsGenerateVideo { get; set; } = true;
      public int VideoCount { get; set; } = 1;
      public int SelectedDuration { get; set; } = 5;
      public string SelectedAspectRatio { get; set; } = "9:16";
      public List<string> CsvPrompts { get; set; } = new();
      public List<IBrowserFile> LocalVideoFiles { get; set; } = new();
      
      // 🔥 새로 추가: 이미지 파일 리스트
      public List<IBrowserFile> SelectedImages { get; set; } = new();
  }

    public class PostProcessingOptions
    {
        public bool EnablePostProcessing { get; set; } = false;
        public bool AddCaption { get; set; } = false;
        public bool UseRandomCaption { get; set; } = false;
        public string CaptionText { get; set; } = "Runmoa.com";
        public List<string> CaptionCsvList { get; set; } = new();
        public string CaptionPosition { get; set; } = "random";
        public string CaptionSize { get; set; } = "random";
        public string CaptionColor { get; set; } = "random";
        public bool AddBackgroundMusic { get; set; } = false;
        public List<IBrowserFile> SelectedMusicFiles { get; set; } = new();
        public float MusicVolume { get; set; } = 0.7f;
    }

    public class VideoGenerationResult
    {
        public string VideoPath { get; set; }
        public string FileName { get; set; }
        public string Prompt { get; set; }
        public string CombinedPrompt { get; set; }
        public string VideoUrl { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 영상 생성 또는 로컬 파일 처리
    /// </summary>
    public async Task<VideoGenerationResult> GenerateOrProcessVideoAsync(
        int videoIndex,
        VideoGenerationOptions genOptions,
        PostProcessingOptions postOptions,
        Action<string> updateStatus = null)
    {
        try
        {
            if (!genOptions.IsGenerateVideo)
            {
                return await ProcessLocalVideoAsync(videoIndex, genOptions, postOptions, updateStatus);
            }
            else
            {
                return await GenerateAIVideoAsync(videoIndex, genOptions, postOptions, updateStatus);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"영상 생성/처리 실패: {ex.Message}");
            return new VideoGenerationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 로컬 파일 처리
    /// </summary>
    private async Task<VideoGenerationResult> ProcessLocalVideoAsync(
        int videoIndex,
        VideoGenerationOptions genOptions,
        PostProcessingOptions postOptions,
        Action<string> updateStatus)
    {
        if (videoIndex > genOptions.LocalVideoFiles.Count)
        {
            throw new Exception("인덱스가 파일 개수를 초과했습니다.");
        }

        var fileToUpload = genOptions.LocalVideoFiles[videoIndex - 1];
        updateStatus?.Invoke($"로컬 파일: {fileToUpload.Name}");

        string tempDir = Path.GetTempPath();
        string fileName = $"local_{DateTime.Now:yyyyMMdd_HHmmss}_{videoIndex:D2}.mp4";
        string localPath = Path.Combine(tempDir, fileName);

        using (var fileStream = new FileStream(localPath, FileMode.Create))
        {
            await fileToUpload.OpenReadStream(maxAllowedSize: 2L * 1024 * 1024 * 1024)
                .CopyToAsync(fileStream);
        }

        string finalPath = localPath;
        if (postOptions.EnablePostProcessing && (postOptions.AddCaption || postOptions.AddBackgroundMusic))
        {
            string processedPath = await ProcessVideoAsync(localPath, fileToUpload.Name, postOptions, updateStatus);
            if (File.Exists(localPath)) File.Delete(localPath);
            finalPath = processedPath;
        }

        return new VideoGenerationResult
        {
            Success = true,
            VideoPath = finalPath,
            FileName = Path.GetFileName(finalPath),
            Prompt = fileToUpload.Name,
            CombinedPrompt = $"로컬 파일: {fileToUpload.Name}"
        };
    }

    /// <summary>
    /// AI 영상 생성
    /// </summary>
    // VideoGenerationService.cs의 GenerateAIVideoAsync 메서드 수정
  // VideoGenerationService.cs의 GenerateAIVideoAsync 메서드 수정
  private async Task<VideoGenerationResult> GenerateAIVideoAsync(
      int videoIndex,
      VideoGenerationOptions genOptions,
      PostProcessingOptions postOptions,
      Action<string> updateStatus)
  {
      if (genOptions.CsvPrompts.Count == 0)
      {
          throw new Exception("CSV 프롬프트가 로드되지 않았습니다.");
      }
  
      string selectedPrompt = genOptions.CsvPrompts[_random.Next(genOptions.CsvPrompts.Count)];
      updateStatus?.Invoke(selectedPrompt.Length > 50 ? selectedPrompt.Substring(0, 50) + "..." : selectedPrompt);
  
      string combinedPrompt = ConfigManager.CombinePrompts(selectedPrompt);
  
      var config = ConfigManager.GetConfig();
      var replicateClient = new ReplicateClient(config.ReplicateApiKey);
  
      // 🔥 이미지 처리 로직 수정 - 임시 URL로 업로드
      string imageUrl = null;
      if (genOptions.SelectedImages.Count > 0)
      {
          try
          {
              // 랜덤하게 이미지 선택
              var selectedImage = genOptions.SelectedImages[_random.Next(genOptions.SelectedImages.Count)];
              
              updateStatus?.Invoke($"이미지 업로드 중: {selectedImage.Name}");
              Console.WriteLine($"=== 선택된 이미지: {selectedImage.Name} ({VideoGenerationService.FormatFileSize(selectedImage.Size)})");
  
              // 🔥 이미지를 임시 파일로 저장하고 URL 생성
              string tempDir = Path.Combine(Path.GetTempPath(), "TempImages");
              Directory.CreateDirectory(tempDir);
              
              string extension = Path.GetExtension(selectedImage.Name).ToLower();
              if (string.IsNullOrEmpty(extension))
              {
                  extension = selectedImage.ContentType switch
                  {
                      "image/jpeg" => ".jpg",
                      "image/png" => ".png",
                      "image/gif" => ".gif",
                      "image/webp" => ".webp",
                      _ => ".jpg"
                  };
              }
              
              string tempImagePath = Path.Combine(tempDir, $"temp_image_{Guid.NewGuid()}{extension}");
              
              // 이미지를 임시 파일로 저장
              using (var imageStream = selectedImage.OpenReadStream(10 * 1024 * 1024)) // 10MB 제한
              using (var fileStream = new FileStream(tempImagePath, FileMode.Create))
              {
                  await imageStream.CopyToAsync(fileStream);
              }
              
              Console.WriteLine($"=== 이미지 임시 저장 완료: {tempImagePath}");
              
              // 🔥 임시로 Base64 사용하되 더 작은 크기로 최적화
              byte[] imageBytes = await File.ReadAllBytesAsync(tempImagePath);
              
              // 파일 크기 체크 (5MB 이상이면 경고)
              if (imageBytes.Length > 5 * 1024 * 1024)
              {
                  Console.WriteLine($"⚠️ 이미지가 큽니다 ({imageBytes.Length / 1024 / 1024}MB). 압축을 고려해보세요.");
              }
              
              string mimeType = selectedImage.ContentType;
              if (string.IsNullOrEmpty(mimeType))
              {
                  mimeType = extension switch
                  {
                      ".jpg" or ".jpeg" => "image/jpeg",
                      ".png" => "image/png",
                      ".gif" => "image/gif",
                      ".webp" => "image/webp",
                      _ => "image/jpeg"
                  };
              }
              
              // Base64 인코딩 (data URI 형식)
              string base64String = Convert.ToBase64String(imageBytes);
              imageUrl = $"data:{mimeType};base64,{base64String}";
              
              Console.WriteLine($"=== 이미지 Base64 변환 완료: {base64String.Length}자");
              
              // 임시 파일 삭제
              try
              {
                  File.Delete(tempImagePath);
              }
              catch (Exception delEx)
              {
                  Console.WriteLine($"=== 임시 파일 삭제 실패: {delEx.Message}");
              }
          }
          catch (Exception ex)
          {
              Console.WriteLine($"=== 이미지 처리 실패: {ex.Message}");
              updateStatus?.Invoke("이미지 처리 실패, 텍스트만으로 진행...");
              // 이미지 처리 실패해도 텍스트로 계속 진행
          }
      }
  
      var request = new ReplicateClient.VideoGenerationRequest
      {
          prompt = combinedPrompt,
          image = imageUrl, // 🔥 data URI 형식의 Base64 이미지
          duration = genOptions.SelectedDuration,
          aspect_ratio = genOptions.SelectedAspectRatio,
          resolution = "1080p",
          fps = 24,
          camera_fixed = true
      };
  
      Console.WriteLine($"=== Replicate 요청 생성:");
      Console.WriteLine($"    프롬프트: {combinedPrompt}");
      Console.WriteLine($"    이미지: {(imageUrl != null ? $"포함됨 ({imageUrl.Length}자)" : "없음")}");
      Console.WriteLine($"    시간: {genOptions.SelectedDuration}초");
      Console.WriteLine($"    비율: {genOptions.SelectedAspectRatio}");
  
      var prediction = await replicateClient.StartVideoGeneration(request);
  
      var progress = new Progress<ReplicateClient.ProgressInfo>(progressInfo =>
      {
          updateStatus?.Invoke($"영상 {videoIndex} - {progressInfo.Status}");
      });
  
      var result = await replicateClient.WaitForCompletion(prediction.id, progress);
  
      if (result.output == null)
      {
          throw new Exception("영상 생성 결과를 받지 못했습니다.");
      }
  
      string videoUrl = result.output.ToString();
      string fileName = $"ai_{DateTime.Now:yyyyMMdd_HHmmss}_{videoIndex:D2}.mp4";
      string localPath = await DownloadVideoAsync(videoUrl, fileName);
  
      string finalPath = localPath;
      if (postOptions.EnablePostProcessing && (postOptions.AddCaption || postOptions.AddBackgroundMusic))
      {
          string processedPath = await ProcessVideoAsync(localPath, selectedPrompt, postOptions, updateStatus);
          if (File.Exists(localPath)) File.Delete(localPath);
          finalPath = processedPath;
      }
  
      return new VideoGenerationResult
      {
          Success = true,
          VideoPath = finalPath,
          FileName = Path.GetFileName(finalPath),
          Prompt = selectedPrompt,
          CombinedPrompt = combinedPrompt,
          VideoUrl = videoUrl
      };
  }

    /// <summary>
    /// 영상 다운로드
    /// </summary>
    private async Task<string> DownloadVideoAsync(string videoUrl, string fileName)
    {
        using var httpClient = new System.Net.Http.HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        string tempFolder = Path.GetTempPath();
        string safeFileName = $"download_{Guid.NewGuid()}.mp4";
        string localPath = Path.Combine(tempFolder, safeFileName);

        byte[] videoBytes = await httpClient.GetByteArrayAsync(videoUrl);
        await File.WriteAllBytesAsync(localPath, videoBytes);

        return localPath;
    }

    /// <summary>
    /// 영상 후처리
    /// </summary>
    private async Task<string> ProcessVideoAsync(
        string inputPath,
        string promptText,
        PostProcessingOptions options,
        Action<string> updateStatus)
    {
        string outputPath = inputPath.Replace(".mp4", "_processed.mp4");

        // 랜덤 값 결정
        string actualPosition = options.CaptionPosition == "random" 
            ? new[] { "top", "center", "bottom" }[_random.Next(3)] 
            : options.CaptionPosition;

        string actualSize = options.CaptionSize == "random"
            ? new[] { "60", "80", "120" }[_random.Next(3)]
            : options.CaptionSize;

        string actualColor = options.CaptionColor == "random"
            ? new[] { "white", "yellow", "red", "black" }[_random.Next(4)]
            : options.CaptionColor;

        var processingOptions = new VideoPostProcessor.ProcessingOptions
        {
            InputVideoPath = inputPath,
            OutputVideoPath = outputPath
        };

        // 캡션 추가
        if (options.AddCaption)
        {
            string finalCaptionText = options.UseRandomCaption 
                ? GetRandomCaption(options.CaptionCsvList, options.CaptionText)
                : options.CaptionText;

            processingOptions.CaptionText = finalCaptionText;
            processingOptions.FontSize = actualSize;
            processingOptions.FontColor = actualColor;
            processingOptions.CaptionPosition = actualPosition;

            Console.WriteLine($"=== 사용된 캡션: {finalCaptionText}");
        }

        // 배경음악 추가
        if (options.AddBackgroundMusic)
        {
            string musicPath = await GetMusicPathAsync(options.SelectedMusicFiles);
            if (!string.IsNullOrEmpty(musicPath) && File.Exists(musicPath))
            {
                processingOptions.BackgroundMusicPath = musicPath;
                processingOptions.MusicVolume = options.MusicVolume;
            }
        }

        var progress = new Progress<string>(status => updateStatus?.Invoke(status));
        string processedPath = await VideoPostProcessor.ProcessVideoAsync(processingOptions, progress);

        // 원본 삭제
        try
        {
            if (File.Exists(inputPath))
            {
                File.Delete(inputPath);
                Console.WriteLine($"=== 원본 파일 삭제: {Path.GetFileName(inputPath)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== 원본 파일 삭제 실패: {ex.Message}");
        }

        return processedPath;
    }

    /// <summary>
    /// 랜덤 캡션 가져오기
    /// </summary>
    private string GetRandomCaption(List<string> captionList, string defaultCaption)
    {
        if (captionList.Count == 0) return defaultCaption;
        return captionList[_random.Next(captionList.Count)];
    }

    /// <summary>
    /// 배경음악 경로 가져오기
    /// </summary>
    private async Task<string> GetMusicPathAsync(List<IBrowserFile> musicFiles)
    {
        try
        {
            if (musicFiles.Count > 0)
            {
                var randomMusic = musicFiles[_random.Next(musicFiles.Count)];

                string tempDir = Path.Combine(Path.GetTempPath(), "TempMusic");
                Directory.CreateDirectory(tempDir);

                string extension = Path.GetExtension(randomMusic.Name).ToLower();
                if (string.IsNullOrEmpty(extension)) extension = ".mp3";

                string musicPath = Path.Combine(tempDir, $"music_{Guid.NewGuid()}{extension}");

                using (var fileStream = new FileStream(musicPath, FileMode.Create))
                {
                    await randomMusic.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024)
                        .CopyToAsync(fileStream);
                }

                Console.WriteLine($"=== 선택된 음악 사용: {randomMusic.Name}");
                return musicPath;
            }
            else
            {
                string musicPath = await VideoPostProcessor.DownloadSampleMusicAsync();
                Console.WriteLine($"=== 기본 음악 사용");
                return musicPath;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== 배경음악 준비 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// CSV 파일 파싱
    /// </summary>
    public List<string> ParseCsvContent(string content)
    {
        var prompts = new List<string>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var columns = line.Split(',');
            if (columns.Length >= 2)
            {
                var prompt = columns[1].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    prompts.Add(prompt);
                }
            }
        }

        return prompts;
    }

    /// <summary>
    /// 히스토리 항목 생성
    /// </summary>
    public VideoHistoryManager.VideoHistoryItem CreateHistoryItem(
        VideoGenerationResult result,
        VideoGenerationOptions genOptions,
        string status = "완료")
    {
        return new VideoHistoryManager.VideoHistoryItem
        {
            Prompt = result.Prompt,
            FinalPrompt = result.CombinedPrompt,
            Duration = genOptions.IsGenerateVideo ? genOptions.SelectedDuration : 0,
            AspectRatio = genOptions.IsGenerateVideo ? genOptions.SelectedAspectRatio : "로컬 파일",
            VideoUrl = result.VideoUrl ?? "",
            IsRandomPrompt = genOptions.IsGenerateVideo,
            FileName = result.FileName,
            IsDownloaded = false,
            IsUploaded = false,
            Status = status
        };
    }

    /// <summary>
    /// 파일 크기 포맷
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        else if (bytes >= 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0:F2} MB";
        else if (bytes >= 1024)
            return $"{bytes / 1024.0:F2} KB";
        else
            return $"{bytes} bytes";
    }
}
  }

