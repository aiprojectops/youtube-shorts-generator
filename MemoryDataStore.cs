using Google.Apis.Util.Store;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace YouTubeShortsWebApp
{
    /// <summary>
    /// 사용자별 메모리 기반 토큰 저장소
    /// </summary>
    public class MemoryDataStore : IDataStore
    {
        // 사용자 ID별로 토큰 저장 (ConcurrentDictionary = 멀티스레드 안전)
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object>> _storage = new();
        private readonly string _userId;

        public MemoryDataStore(string userId)
        {
            _userId = userId ?? throw new ArgumentNullException(nameof(userId));
            
            // 해당 사용자의 저장소가 없으면 생성
            _storage.TryAdd(_userId, new ConcurrentDictionary<string, object>());
            
            Console.WriteLine($"=== MemoryDataStore 생성: UserId={_userId}");
        }

        public Task StoreAsync<T>(string key, T value)
        {
            if (_storage.TryGetValue(_userId, out var userStore))
            {
                userStore[key] = value;
                Console.WriteLine($"=== 토큰 저장: UserId={_userId}, Key={key}");
            }
            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(string key)
        {
            if (_storage.TryGetValue(_userId, out var userStore) && 
                userStore.TryGetValue(key, out var value))
            {
                Console.WriteLine($"=== 토큰 로드: UserId={_userId}, Key={key}");
                return Task.FromResult((T)value);
            }
            
            Console.WriteLine($"=== 토큰 없음: UserId={_userId}, Key={key}");
            return Task.FromResult(default(T));
        }

        public Task DeleteAsync<T>(string key)
        {
            if (_storage.TryGetValue(_userId, out var userStore))
            {
                userStore.TryRemove(key, out _);
                Console.WriteLine($"=== 토큰 삭제: UserId={_userId}, Key={key}");
            }
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            if (_storage.TryGetValue(_userId, out var userStore))
            {
                userStore.Clear();
                Console.WriteLine($"=== 모든 토큰 삭제: UserId={_userId}");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 사용자 세션 종료 시 토큰 정리
        /// </summary>
        public static void ClearUserData(string userId)
        {
            if (_storage.TryRemove(userId, out _))
            {
                Console.WriteLine($"=== 사용자 데이터 완전 삭제: UserId={userId}");
            }
        }
    }
}
