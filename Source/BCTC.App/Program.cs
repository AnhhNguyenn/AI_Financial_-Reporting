using BCTC.App.Services.BctcServices;
using BCTC.BusinessLogic.OcrLogic;
using BCTC.DataAccess.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text;

namespace BCTC.App
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var builder = Host.CreateApplicationBuilder(args);

            builder.Configuration
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables();

            string root = PathHelper.GetWwwRoot();
            Directory.SetCurrentDirectory(root);

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .CreateLogger();

            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog();

            builder.Services.Configure<BctcOptions>(builder.Configuration.GetSection("Bctc"));
            PathHelper.Init(builder.Configuration);

            builder.Services.PostConfigure<BctcOptions>(opt =>
            {
                opt.ConnectionString = builder.Configuration.GetConnectionString("FinanceFull");
            });
            builder.Services.AddBctcServices(builder.Configuration);
            Log.Information("[Program] START");

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log.Fatal(e.ExceptionObject as Exception, "[Program] Unhandled Exception");
                Log.CloseAndFlush();
            };
            try
            {
                var app = builder.Build();

                using var scope = app.Services.CreateScope();
                var provider = scope.ServiceProvider;
                var runner = provider.GetRequiredService<AutoProcessor>();
                await runner.RunDefaultAsync();

                Log.Information("[Program] STOP – No more reports to process");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[Program] Fatal error");
                Console.WriteLine("Error occurred! Press Enter to exit...");
                Console.ReadLine();
            }
            finally
            {
                Log.Information("[Program] END");
                Log.CloseAndFlush();
            }
        }
    }
}
