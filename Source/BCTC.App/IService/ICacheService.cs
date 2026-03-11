using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Interfaces.Services
{
    public interface ICacheService
    {
        T Get<T>(string key);
        Task<T> GetAsync<T>(string key);
        void Set<T>(string key, T value, TimeSpan? expiration = null);
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
        void Remove(string key);
        Task RemoveAsync(string key);
        bool Exists(string key);
        Task<bool> ExistsAsync(string key);
    }
}
