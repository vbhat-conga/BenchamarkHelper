// See https://aka.ms/new-console-template for more information
using BenchamarkHelper.DbProvider;
using BenchamarkHelper.Ingestor;
using BenchamarkHelper.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;


namespace BenchMarkHelper;
class Program
{
    public static async Task Main()
    {       
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var serviceProvider = ConfigureService(config);
        var redisConfig = serviceProvider.GetService<IOptions<RedisConfiguration>>();

        AnsiConsole.Write(new FigletText("Welcome to Benchmarking").Color(Color.Red));

        var Db = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What's the [green]database you want to benchmark [/]?")
                    .PageSize(10)
                    .MoreChoicesText("[grey](Move up and down to reveal more database)[/]")
                    .AddChoices(new[] {
                        "Redis", "S3"
                    }));

        switch (Db)
        {
            case "Redis":
                var service=serviceProvider.GetService<Func<string, IDBProvider>>();
                var instance = service("Redis");
                await instance.RunBenchmark();
                break;
            case "S3":
                break;
            default:
                throw new InvalidOperationException();
                
        }
    }

    private static ServiceProvider ConfigureService(IConfiguration config)
    {
        var service = new ServiceCollection()
        .AddSingleton(config)
        .Configure<RedisConfiguration>(config.GetSection("Redis"))
        .AddSingleton<IIngestor, KustoIngestor>()
        .AddSingleton<Redis>()
        .AddSingleton<Func<string, IDBProvider>>(serviceProvider => key =>
        {
            switch (key)
            {
                case "Redis":
                    return serviceProvider.GetRequiredService<Redis>();
                case "S3":
                    throw new InvalidOperationException();
                default:
                    throw new InvalidOperationException();
            }
        });
       return service.BuildServiceProvider();
    }

}






