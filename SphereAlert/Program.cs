using SphereAlert.Services.Config;

public class Program
{
    public static async Task Main(string[] args)
    {
        await StartUp.ConfigureApplication();
        WebApplication app = StartUp.CreateWebApp(args);
        await app.RunAsync();
    }
}
