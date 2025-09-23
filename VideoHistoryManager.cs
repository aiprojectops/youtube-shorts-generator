using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace YouTubeShortsWebApp
{
    public class VideoHistoryManager
    {
        // 웹앱에서는 임시 디렉토리 사용
        private static readonly string HistoryFilePath = Path.Combine(
            Path.GetTempPath(),
            "YouTubeShortsWebApp",
            "video_history.json"
        );

        public class VideoHistoryItem
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public string Prompt { get; set; } = "";
            public string FinalPrompt { get; set; } = "";
            public int Duration { get; set; }
            public string AspectRatio { get; set; } = "";
            public string VideoUrl { get; set; } = "";
            public string Status { get; set; } = "완료";
            public bool IsRandomPrompt { get; set; } = false;
            public string FileName { get; set; } = "";
            public bool IsDownloaded { get; set; } = false;
            public bool IsUploaded { get; set; } = false;
            public string YouTubeUrl { get; set; } = "";
        }

        public static List<VideoHistoryItem> GetHistory()
        {
            try
            {
                if (!File.Exists(HistoryFilePath))
                    return new List<VideoHistoryItem>();

                string json = File.ReadAllText(HistoryFilePath);
                var history = JsonConvert.DeserializeObject<List<VideoHistoryItem>>(json) ?? new List<VideoHistoryItem>();

                return history.OrderByDescending(x => x.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"히스토리 로드 오류: {ex.Message}");
                return new List<VideoHistoryItem>();
            }
        }

        public static void AddHistoryItem(VideoHistoryItem item)
        {
            try
            {
                var history = GetHistory();
                history.Insert(0, item);

                // 최대 50개만 유지 (웹앱에서는 메모리 고려)
                if (history.Count > 50)
                {
                    history = history.Take(50).ToList();
                }

                SaveHistory(history);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"히스토리 추가 오류: {ex.Message}");
            }
        }

        public static void UpdateHistoryItem(string id, Action<VideoHistoryItem> updateAction)
        {
            try
            {
                var history = GetHistory();
                var item = history.FirstOrDefault(x => x.Id == id);

                if (item != null)
                {
                    updateAction(item);
                    SaveHistory(history);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"히스토리 업데이트 오류: {ex.Message}");
            }
        }

        public static void DeleteHistoryItem(string id)
        {
            try
            {
                var history = GetHistory();
                var item = history.FirstOrDefault(x => x.Id == id);

                if (item != null)
                {
                    history.Remove(item);
                    SaveHistory(history);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"히스토리 삭제 오류: {ex.Message}");
            }
        }

        public static void ClearHistory()
        {
            try
            {
                SaveHistory(new List<VideoHistoryItem>());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"히스토리 삭제 오류: {ex.Message}");
            }
        }

        private static void SaveHistory(List<VideoHistoryItem> history)
        {
            try
            {
                string directory = Path.GetDirectoryName(HistoryFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonConvert.SerializeObject(history, Formatting.Indented);
                File.WriteAllText(HistoryFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"히스토리 저장 오류: {ex.Message}");
            }
        }
    }
}