using StackToDisco;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddHostedService<Worker>();
        services.Configure<WorkerSettings>(context.Configuration.GetSection("WorkerSettings"));
    })
    .Build();

await host.RunAsync();

class WorkerSettings
{
    public string DiscordChannelId { get; set; } = null!;
    public string DiscordBotToken { get; set; } = null!;
    public TimeSpan QueryInterval { get; set; }
}
