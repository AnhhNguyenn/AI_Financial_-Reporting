using MappingReportNorm.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Services
{
    public class MemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<MemoryCacheService> _logger;
        private readonly TimeSpan _defaultExpiration;

        public MemoryCacheService(
            IMemoryCache cache,
            ILogger<MemoryCacheService> logger,
            TimeSpan? defaultExpiration = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(30);
        }

        public T Get<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            if (_cache.TryGetValue(key, out T value))
            {
                _logger.LogDebug("Cache hit for key: {Key}", key);
                return value;
            }

            _logger.LogDebug("Cache miss for key: {Key}", key);
            return default;
        }

        public Task<T> GetAsync<T>(string key)
        {
            return Task.FromResult(Get<T>(key));
        }

        public void Set<T>(string key, T value, TimeSpan? expiration = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? _defaultExpiration,
                Priority = CacheItemPriority.Normal
            };

            _cache.Set(key, value, cacheOptions);
            _logger.LogDebug("Cached value for key: {Key} with expiration: {Expiration}",
                key, expiration ?? _defaultExpiration);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            Set(key, value, expiration);
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            _cache.Remove(key);
            _logger.LogDebug("Removed cache for key: {Key}", key);
        }

        public Task RemoveAsync(string key)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public bool Exists(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            return _cache.TryGetValue(key, out _);
        }

        public Task<bool> ExistsAsync(string key)
        {
            return Task.FromResult(Exists(key));
        }
    }
}
