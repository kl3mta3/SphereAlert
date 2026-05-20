using System.Net;
using SphereAlert.Data.Database;
using SphereAlert.Data.Repositories;
using SphereAlert.Services.Alerts;
using SphereAlert.Services.Domains;
using SphereAlert.Services.Scheduler;
using SphereAlert.Services.Scripts;

namespace SphereAlert.Services.Config
{
    /// <summary>Application bootstrap: data directory, database, DI graph, and the web host.</summary>
    public class StartUp
    {
        public static async Task ConfigureApplication()
        {
            ConfigureService.Load();
            await DatabaseManager.Initialize();
            ConfigureService.IsSetup = true;
        }

        public static WebApplication CreateWebApp(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Error);

            builder.Services.AddRazorPages();
            builder.Services.AddControllers();

            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromHours(8);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Name = "SphereAlert.Session";
            });

            // Singletons
            builder.Services.AddSingleton<Logger>();
            builder.Services.AddSingleton<ScriptService>();
            builder.Services.AddSingleton<ZipInjectionService>();

            // Per-request data + orchestration services
            builder.Services.AddScoped<UserRepository>();
            builder.Services.AddScoped<ProviderRepository>();
            builder.Services.AddScoped<DomainRepository>();
            builder.Services.AddScoped<AlertRepository>();
            builder.Services.AddScoped<HistoryRepository>();
            builder.Services.AddScoped<AlertService>();
            builder.Services.AddScoped<DomainImportService>();
            builder.Services.AddScoped<ScriptInstallDetector>();

            // Background worker that expires timed alerts
            builder.Services.AddHostedService<AlertSchedulerService>();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Any, ConfigureService.ServerPort);
            });

            var app = builder.Build();

            app.UseStaticFiles();
            app.UseSession();
            app.UseRouting();

            app.MapRazorPages();
            app.MapControllers();

            return app;
        }
    }
}
