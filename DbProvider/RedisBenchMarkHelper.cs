using BenchamarkHelper.Ingestor;
using BenchamarkHelper.Model;
using Kusto.Cloud.Platform.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Spectre.Console;
using System.IO;

namespace BenchamarkHelper.DbProvider
{
    internal class Redis : IDBProvider
    {
        const string PrivateKeyFilePath = @"C:\.ssh\id_rsa";

        private readonly RedisConfiguration _redisConfiguration;
        private readonly IIngestor _ingestor;
        public Redis(IOptions<RedisConfiguration> options, IIngestor ingestor)
        {
            _redisConfiguration = options.Value;
            _ingestor = ingestor;
        }

        public async Task RunBenchmark()
        {
            do
            {

                var env = AnsiConsole.Prompt(
                       new SelectionPrompt<string>()
                           .Title("Please select the performance environment to run benchmark?")
                           .PageSize(10)
                           .MoreChoicesText("[grey](Move up and down to reveal more database)[/]")
                           .AddChoices(new[] {
                        "Gating", "Lab","Custom"
                           }));

                var isStandard = AnsiConsole.Confirm("Would you like to run standard benchmarking");
                try
                {

                    var directory = new DirectoryInfo("./Results");
                    directory.EnumerateFiles().ForEach(file=> file.Delete());
                    RunLoadTest(env, directory, isStandard);
                    await _ingestor.IngestData(directory);
                }
                catch (Exception ex)
                {
                    AnsiConsole.Markup($"[red]{ex.Message}[/]\n");
                }
            } while (AnsiConsole.Confirm("Do you want to continue?"));
        }

        private void RunLoadTest(string env, DirectoryInfo directory, bool isStandar = true)
        {
            var commandText = string.Empty;
            var testId = $"Test_{Guid.NewGuid()}";
            var installcommandText = File.ReadAllText("./memtier.sh");
            var con = GetConnection(_redisConfiguration, env);
            using (var sshClient = new SshClient(con))
            {
                AnsiConsole.Status()
               .Start("Connecting to host...", ctx =>
               {
                   sshClient.Connect();
               });
                AnsiConsole.MarkupLine("connected to host\n");
                AnsiConsole.Status()
               .Start("Installing bench mark tool...", ctx =>
               {
                   ctx.Spinner(Spinner.Known.Circle);
                   var command = sshClient.RunCommand(installcommandText);
                   var result = command.Execute();
               });
                AnsiConsole.Markup("Tool has been installed or already installed.\n");

                AnsiConsole.MarkupLine($"\n[white]***************************** Starting load test **************************[/]\n");

                var server = GetServer(env, _redisConfiguration);
                if (isStandar)
                {
                    foreach (var load in _redisConfiguration.DefaultLoad)
                    {
                        testId = $"Test_{Guid.NewGuid()}";                       
                        var cmd = GetCommand(_redisConfiguration, testId, env,server, load, true);
                        AnsiConsole.Status()
                         .Start("Running Benchmark...", ctx =>
                         {
                             ctx.Spinner(Spinner.Known.Circle);
                             var command = sshClient.RunCommand(cmd);
                             var result = command.Execute();
                             AnsiConsole.MarkupLine($"{result}\n");
                         });

                        DownloadResult(con, testId, directory);
                        AnsiConsole.WriteLine();
                    }
                }
                else
                {
                    commandText = GetCommand(_redisConfiguration, testId, env, server);
                    AnsiConsole.Status()
                    .Start("Running Benchmark...", ctx =>
                    {
                        ctx.Spinner(Spinner.Known.Circle);
                        var command = sshClient.RunCommand(commandText);
                        var result = command.Execute();
                        AnsiConsole.MarkupLine($"{result}\n");
                    });
                    DownloadResult(con, testId, directory);
                }
                AnsiConsole.MarkupLine($"\n[white]***************************** Load test Completed **************************[/]\n");
            }
        }

        private void DownloadResult(ConnectionInfo? con, string testId, DirectoryInfo directory)
        {
            if (con == null)
                return;

            AnsiConsole.Status()
                    .Start("Downloading results under RESULTS folder", ctx =>
                    {
                        ctx.Spinner(Spinner.Known.Circle);
                        using var scpclient = new ScpClient(con);
                        scpclient.Connect();
                        try
                        {
                            scpclient.Download($"/home/{con.Username}/{testId}", directory);
                        }
                        catch (Exception ex)
                        {
                            scpclient.Download($"/home/{con.Username}/memtier_benchmark/{testId}", directory);
                        }
                    });
            AnsiConsole.Markup($"Result is downloaded for Test: {testId}\n");
        }

      
        private static string GetCommand(RedisConfiguration configuration, string testId, string env, string server, string? defaultCommand = null, bool isStandard = false)
        {
            if (!isStandard)
            {
                var payloadSize = AnsiConsole.Prompt(new TextPrompt<int>("Please provide payload size in bytes :\n").DefaultValue(2000));
                var threads = AnsiConsole.Prompt(new TextPrompt<int>("Please provide the number of thread :\n").DefaultValue(2));
                var clients = AnsiConsole.Prompt(new TextPrompt<int>("Please provide the number of connections per thread :\n").DefaultValue(2));
                var testDuration = AnsiConsole.Prompt(new TextPrompt<int>("Please provide the test duration in second :\n").DefaultValue(300));
                var setGetRatio = AnsiConsole.Prompt(new TextPrompt<string>("Set:Get ratio :\n").DefaultValue("1:10"));
                return string.Format(configuration.CustomLoad, server, clients, threads, payloadSize, $"{testId}", testDuration, setGetRatio);
            }
            else if (defaultCommand != null)
            {
                return string.Format(defaultCommand, server, testId);
            }
            return string.Empty;                       
        }

        private static string GetServer(string env, RedisConfiguration configuration)
        {
            var server = string.Empty;
            switch (env)
            {
                case "Gating": 
                        server=configuration.Gating.Server; 
                        break;
                case "Lab": 
                        server = configuration.Lab.Server; 
                        break;
                case "Custom":
                    server = AnsiConsole.Prompt(new TextPrompt<string>("Please provide redis server to do benchmark :\n").DefaultValue("usw2-redis-perf-app-data01-rls06.ghgouu.clustercfg.usw2.cache.amazonaws.com"));
                    break;
            }
            return server;
        }

        private static ConnectionInfo? GetConnection(RedisConfiguration configuration, string env)
        {
            var privateKeyPath = AnsiConsole.Prompt(new TextPrompt<string>("Please provide the private key path :\n").DefaultValue(PrivateKeyFilePath));
            var privateKeyFile = new PrivateKeyFile(configuration.PrivateKeyPath);
            switch (env)
            {
                case "Gating": return new ConnectionInfo(configuration.Gating.Host, configuration.Gating.User, new PrivateKeyAuthenticationMethod(configuration.Gating.User, privateKeyFile));
                case "Lab": return new ConnectionInfo(configuration.Lab.Host, configuration.Lab.User, new PrivateKeyAuthenticationMethod(configuration.Lab.User, privateKeyFile));
                case "Custom":                   
                    var host = AnsiConsole.Prompt(new TextPrompt<string>("Please provide the host :\n").DefaultValue("44.225.28.27")); ;
                    var userName = AnsiConsole.Prompt(new TextPrompt<string>("Please provide the user name :\n").DefaultValue("clouduser"));
                    var customprivateKeyFile = new PrivateKeyFile(privateKeyPath);
                    return new ConnectionInfo(host, userName, new PrivateKeyAuthenticationMethod(userName, customprivateKeyFile));
                default: return null;

            }
        }
    }
}
