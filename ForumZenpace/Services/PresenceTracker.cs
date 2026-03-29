using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace ForumZenpace.Services
{
    public class UserPresenceState
    {
        public DateTime LastHeartbeat { get; set; }
        public List<string> ConnectionIds { get; set; } = new();
    }

    public sealed class PresenceTracker
    {
        private readonly IDistributedCache _cache;
        private const string CacheKey = "presence:state";
        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(45);
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public PresenceTracker(IDistributedCache cache)
        {
            _cache = cache;
        }

        private async Task<Dictionary<int, UserPresenceState>> GetStateAsync()
        {
            var data = await _cache.GetStringAsync(CacheKey);
            return string.IsNullOrEmpty(data) ? new Dictionary<int, UserPresenceState>() : JsonSerializer.Deserialize<Dictionary<int, UserPresenceState>>(data)!;
        }

        private async Task SaveStateAsync(Dictionary<int, UserPresenceState> state)
        {
            await _cache.SetStringAsync(CacheKey, JsonSerializer.Serialize(state), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });
        }

        public async Task<bool> UserConnectedAsync(int userId, string connectionId)
        {
            await _semaphore.WaitAsync();
            try
            {
                var state = await GetStateAsync();
                bool isFirst = false;

                if (!state.ContainsKey(userId))
                {
                    state[userId] = new UserPresenceState { LastHeartbeat = DateTime.UtcNow };
                    isFirst = true;
                }
                
                var userState = state[userId];
                if (!userState.ConnectionIds.Contains(connectionId))
                {
                    userState.ConnectionIds.Add(connectionId);
                }
                userState.LastHeartbeat = DateTime.UtcNow;

                await SaveStateAsync(state);
                return isFirst;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<bool> UserDisconnectedAsync(int userId, string connectionId)
        {
            await _semaphore.WaitAsync();
            try
            {
                var state = await GetStateAsync();
                if (!state.TryGetValue(userId, out var userState))
                {
                    return true;
                }

                userState.ConnectionIds.Remove(connectionId);
                if (userState.ConnectionIds.Count == 0 || (DateTime.UtcNow - userState.LastHeartbeat) >= HeartbeatTimeout)
                {
                    state.Remove(userId);
                    await SaveStateAsync(state);
                    return true;
                }

                await SaveStateAsync(state);
                return false;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task HeartbeatAsync(int userId)
        {
            await _semaphore.WaitAsync();
            try
            {
                var state = await GetStateAsync();
                if (!state.ContainsKey(userId))
                {
                    state[userId] = new UserPresenceState();
                }

                state[userId].LastHeartbeat = DateTime.UtcNow;

                var now = DateTime.UtcNow;
                var deadKeys = state.Where(kvp => (now - kvp.Value.LastHeartbeat) >= HeartbeatTimeout).Select(k => k.Key).ToList();
                foreach (var key in deadKeys) state.Remove(key);

                await SaveStateAsync(state);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<int[]> GetOnlineUsersAsync()
        {
            var state = await GetStateAsync();
            var now = DateTime.UtcNow;
            return state.Where(kvp => kvp.Value.ConnectionIds.Count > 0 && (now - kvp.Value.LastHeartbeat) < HeartbeatTimeout).Select(kvp => kvp.Key).ToArray();
        }
    }
}
