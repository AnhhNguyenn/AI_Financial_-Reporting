using BCTC.App.IService;
using BCTC.App.Services.BctcServices;
using BCTC.App.Services.GeminiServices;
using BCTC.App.Services.InputServices;
using BCTC.App.Services.MappingCache;
using BCTC.App.Services.ScanFix;
using BCTC.DataAccess;
using BCTC.DataAccess.Models;
using BCTC.DataAccess.Repositories.Interfaces;
using MappingReportNorm.Interfaces.Services;
using MappingReportNorm.Repositories;
using MappingReportNorm.Services;
using MappingReportNorm.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BCTC.App
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddBctcServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddHttpClient<GeminiService>(c =>
            {
                c.Timeout = TimeSpan.FromMinutes(50);
            });
            services.AddTransient<IBctcservice, Bctcservice>();
            services.AddTransient<InputService>();
            services.AddTransient<AutoProcessor>();
            services.AddTransient<InputRepository>(sp =>
            {
                var opt = sp.GetRequiredService<IOptions<BctcOptions>>().Value;
                return new InputRepository(opt.ConnectionString);
            });

            services.Configure<MappingSettings>(config.GetSection("MappingSettings"));

            services.AddMemoryCache();
            services.AddSingleton<ICacheService>(sp =>
            {
                var cache = sp.GetRequiredService<IMemoryCache>();
                var logger = sp.GetRequiredService<ILogger<MemoryCacheService>>();
                return new MemoryCacheService(cache, logger, TimeSpan.FromMinutes(30));
            });
            services.AddScoped<IFinanceFullRepository, FinanceFullRepository>();
            services.AddScoped<IFinancialService, FinancialService>();
            services.AddScoped<IFinancialMappingService, FinancialMappingService>();
            services.AddScoped<IFinancialReMappingService, FinancialReMappingService>();
            services.AddScoped<INormalizationService, NormalizationService>();
            services.AddTransient<DocumentCleaner>();

            services.AddSingleton<IFileMappingCacheService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<FileMappingCacheService>>();
                var wwwRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
                return new FileMappingCacheService(logger, TimeSpan.FromDays(180));
            });

            services.AddHostedService<CacheCleanupBackgroundService>();

            return services;
        }
    }
}