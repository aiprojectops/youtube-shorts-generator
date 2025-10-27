using Google.Apis.Util.Store;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace YouTubeShortsWebApp
{
    /// <summary>
    /// 전역 공유 메모리 기반 토큰 저장소 (Thread-Safe)
    /// 모든 사용자의 토큰을 UserId별로 격리하여 저장
    /// </summary>
    public class SharedMemoryDataStore : IDataStore
    {
        // UserId -> (Key -> TokenData) 구조
        // ConcurrentDictionary는 자동으로 Thread-Safe
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _storage;

        public SharedMemoryDataStore()
        {
            _storage = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();
            Console.WriteLine("=== ✅ SharedMemoryDataStore 싱글톤 생성됨");
        }

        /// <summary>
        /// 토큰 저장
        /// </summary>
        public Task StoreAsync<T>(string key, T value)
        {
            var userId = ExtractUserId(key);
            var actualKey = ExtractActualKey(key);

            // UserId별 저장소가 없으면 생성
            var userStorage = _storage.GetOrAdd(userId, _ => new ConcurrentDictionary<string, string>());

            var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(value);
            userStorage[actualKey] = serialized;

            Console.WriteLine($"=== 💾 토큰 저장: UserId={userId}, Key={actualKey}, 전체 사용자 수={_storage.Count}");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 토큰 로드
        /// </summary>
        public Task<T> GetAsync<T>(string key)
        {
            var userId = ExtractUserId(key);
            var actualKey = ExtractActualKey(key);

            if (_storage.TryGetValue(userId, out var userStorage) &&
                userStorage.TryGetValue(actualKey, out var serialized))
            {
                var result = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(serialized);
                Console.WriteLine($"=== 📥 토큰 로드 성공: UserId={userId}, Key={actualKey}");
                return Task.FromResult(result);
            }

            Console.WriteLine($"=== ⚠️ 토큰 없음: UserId={userId}, Key={actualKey}");
            return Task.FromResult(default(T));
        }

        /// <summary>
        /// 토큰 삭제
        /// </summary>
        public Task DeleteAsync<T>(string key)
        {
            var userId = ExtractUserId(key);
            var actualKey = ExtractActualKey(key);

            if (_storage.TryGetValue(userId, out var userStorage))
            {
                userStorage.TryRemove(actualKey, out _);
                Console.WriteLine($"=== 🗑️ 토큰 삭제: UserId={userId}, Key={actualKey}");

                // 해당 사용자의 토큰이 모두 삭제되면 사용자 저장소도 제거
                if (userStorage.IsEmpty)
                {
                    _storage.TryRemove(userId, out _);
                    Console.WriteLine($"=== 🗑️ 사용자 저장소 제거: UserId={userId}");
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 전체 토큰 삭제
        /// </summary>
        public Task ClearAsync()
        {
            _storage.Clear();
            Console.WriteLine("=== 🗑️ 모든 토큰 삭제됨");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Key에서 UserId 추출
        /// 형식: "UserId::ActualKey" 또는 "ActualKey" (기본 UserId 사용)
        /// </summary>
        private string ExtractUserId(string key)
        {
            if (key.Contains("::"))
            {
                return key.Split("::")[0];
            }
            return "default"; // UserId가 없으면 기본값
        }

        /// <summary>
        /// Key에서 실제 키 추출
        /// </summary>
        private string ExtractActualKey(string key)
        {
            if (key.Contains("::"))
            {
                return key.Split("::")[1];
            }
            return key;
        }

        /// <summary>
        /// 디버깅: 현재 저장된 사용자 목록
        /// </summary>
        public void PrintStatus()
        {
            Console.WriteLine($"=== 📊 저장소 상태: 총 {_storage.Count}명의 사용자");
            foreach (var userId in _storage.Keys)
            {
                var tokenCount = _storage[userId].Count;
                Console.WriteLine($"    - {userId}: {tokenCount}개 토큰");
            }
        }
    }
}
