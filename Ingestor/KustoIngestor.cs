using BenchamarkHelper.Model;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Linq;
using Kusto.Data.Net.Client;
using Kusto.Ingest;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Spectre.Console;

namespace BenchamarkHelper.Ingestor
{
    public class KustoIngestor : IIngestor
    {
        private const string ConfigFileName = @"kustoSettings.json";
        private static int Step = 1;
        private static bool WaitForUser;

        public KustoIngestor()
        {

        }


        public async Task IngestData(DirectoryInfo directory)
        {
            try
            {
                AnsiConsole.MarkupLine($"\n[white]***************************** Data ingestion started **************************[/]\n");
                var config = LoadConfigs(ConfigFileName);
                WaitForUser = config.WaitForUser;
                if (config.AuthenticationMode == AuthenticationModeOptions.UserPrompt)
                {
                    WaitForUserToProceed("You will be prompted *twice* for credentials during this script. Please return to the console after authenticating.");
                }

                var kustoConnectionString = Utils.Authentication.GenerateConnectionString(config.KustoUri, config.AuthenticationMode, config.TenantId);
                var ingestConnectionString = Utils.Authentication.GenerateConnectionString(config.IngestUri, config.AuthenticationMode, config.TenantId);

                using (var adminClient = KustoClientFactory.CreateCslAdminProvider(kustoConnectionString)) // For control commands
                using (var queryProvider = KustoClientFactory.CreateCslQueryProvider(kustoConnectionString)) // For regular querying
                using (var ingestClient = KustoIngestFactory.CreateQueuedIngestClient(ingestConnectionString)) // For ingestion
                {
                    await PreIngestionQueryingAsync(config, adminClient, queryProvider);

                    if (config.IngestData)
                    {
                        await IngestionAsync(config, adminClient, ingestClient, directory);
                    }

                    if (config.QueryData)
                    {
                        await PostIngestionQueryingAsync(queryProvider, config.DatabaseName, config.TableName, config.IngestData);
                    }
                }
                AnsiConsole.MarkupLine($"\n[white]***************************** Data ingestion completed **************************[/]\n");
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
            }
        }
        /// <summary>
        /// Loads JSON configuration file, and sets the metadata in place. 
        /// </summary>
        /// <param name="configFilePath"> Configuration file path.</param>
        /// <returns>ConfigJson object, allowing access to the metadata fields.</returns>
        public static ConfigJson LoadConfigs(string configFilePath)
        {
            try
            {
                var json = File.ReadAllText(configFilePath);
                var config = JsonConvert.DeserializeObject<ConfigJson>(json);
                var missing = new[]
                {
                    (name: nameof(config.DatabaseName), value: config.DatabaseName),
                    (name: nameof(config.TableName), value: config.TableName),
                    (name: nameof(config.TableSchema), value: config.TableSchema),
                    (name: nameof(config.KustoUri), value: config.KustoUri),
                    (name: nameof(config.IngestUri), value: config.IngestUri)
                }.Where(item => string.IsNullOrWhiteSpace(item.value)).ToArray();

                if (missing.Any())
                {
                    Utils.ErrorHandler($"File '{configFilePath}' is missing required fields: {string.Join(", ", missing.Select(item => item.name))}");
                }

                if (config.Data is null)
                {
                    Utils.ErrorHandler($"Required field Data in '{configFilePath}' is either missing, empty or misfilled");
                }
                return config;
            }
            catch (Exception ex)
            {
                Utils.ErrorHandler($"Couldn't read config file: '{configFilePath}'", ex);
            }

            throw new InvalidOperationException("Unreachable code");
        }

        /// <summary>
        /// First phase, pre ingestion - will reach the provided DB with several control commands and a query based on the configuration File.
        /// </summary>
        /// <param name="config">ConfigJson object containing the SampleApp configuration</param>
        /// <param name="adminClient">Privileged client to run Control Commands</param>
        /// <param name="queryProvider">Client to run queries</param>
        private static async Task PreIngestionQueryingAsync(ConfigJson config, ICslAdminProvider adminClient, ICslQueryProvider queryProvider)
        {
            if (config.UseExistingTable)
            {
                if (config.AlterTable)
                {
                    // Tip: Usually table was originally created with a schema appropriate for the data being ingested, so this wouldn't be needed.
                    // Learn More: For more information about altering table schemas, see:
                    // https://docs.microsoft.com/azure/data-explorer/kusto/management/alter-table-command
                    WaitForUserToProceed($"Alter-merge existing table '{config.DatabaseName}.{config.TableName}' to align with the provided schema");
                    await AlterMergeExistingTableToProvidedSchemaAsync(adminClient, config.DatabaseName, config.TableName, config.TableSchema);
                }
                if (config.QueryData)
                {
                    WaitForUserToProceed($"Get existing row count in '{config.DatabaseName}.{config.TableName}'");
                    await QueryExistingNumberOfRowsAsync(queryProvider, config.DatabaseName, config.TableName);
                }
            }
            else
            {
                // Tip: This is generally a one-time configuration
                // Learn More: For more information about creating tables, see: https://docs.microsoft.com/azure/data-explorer/one-click-table
                WaitForUserToProceed($"Create table '{config.DatabaseName}.{config.TableName}'");
                await CreateNewTableAsync(adminClient, config.DatabaseName, config.TableName, config.TableSchema);
            }

            // Learn More: Kusto batches data for ingestion efficiency. The default batching policy ingests data when one of the following conditions are met:
            //   1) More than 1,000 files were queued for ingestion for the same table by the same user
            //   2) More than 1GB of data was queued for ingestion for the same table by the same user
            //   3) More than 5 minutes have passed since the first File was queued for ingestion for the same table by the same user
            //  For more information about customizing the ingestion batching policy, see:
            // https://docs.microsoft.com/azure/data-explorer/kusto/management/batchingpolicy
            // TODO: Change if needed. Disabled to prevent an existing batching policy from being unintentionally changed
            if (false && config.BatchingPolicy !=null)
            {
                WaitForUserToProceed($"Alter the batching policy for table '{config.DatabaseName}.{config.TableName}'");
                await AlterBatchingPolicyAsync(adminClient, config.DatabaseName, config.TableName, config.BatchingPolicy);
            }

        }

        /// <summary>
        /// Alter-merges the given existing table to provided schema.
        /// </summary>
        /// <param name="adminClient">Privileged client to run Control Commands</param>
        /// <param name="configDatabaseName">DB name</param>
        /// <param name="configTableName">Table name</param>
        /// <param name="configTableSchema">Table Schema</param>
        private static async Task AlterMergeExistingTableToProvidedSchemaAsync(ICslAdminProvider adminClient, string configDatabaseName, string configTableName, string configTableSchema)
        {
            // You can also use the CslCommandGenerator class to build commands: string command = CslCommandGenerator.GenerateTableAlterMergeCommand();
            var command = $".alter-merge table {configTableName} {configTableSchema}";
            await Utils.Queries.ExecuteAsync(adminClient, configDatabaseName, command);
        }

        /// <summary>
        /// Creates a new table.
        /// </summary>
        /// <param name="adminClient">Privileged client to run Control Commands</param>
        /// <param name="configDatabaseName">DB name</param>
        /// <param name="configTableName">Table name</param>
        /// <param name="configTableSchema">Table Schema</param>
        private static async Task CreateNewTableAsync(ICslAdminProvider adminClient, string configDatabaseName, string configTableName, string configTableSchema)
        {
            // You can also use the CslCommandGenerator class to build commands: string command = CslCommandGenerator.GenerateTableCreateCommand();
            var command = $".create table {configTableName} {configTableSchema}";
            await Utils.Queries.ExecuteAsync(adminClient, configDatabaseName, command);
        }

        /// <summary>
        /// Alters the batching policy based on BatchingPolicy in configuration.
        /// </summary>
        /// <param name="adminClient">Privileged client to run Control Commands</param>
        /// <param name="configDatabaseName">DB name</param>
        /// <param name="configTableName">Table name</param>
        /// <param name="batchingPolicy">Ingestion batching policy</param>
        private static async Task AlterBatchingPolicyAsync(ICslAdminProvider adminClient, string configDatabaseName, string configTableName, BatchingPolicy batchingPolicy)
        {

            var command = CslCommandGenerator.GenerateTableAlterIngestionBatchingPolicyCommand(
               configDatabaseName,
               configTableName,
               new IngestionBatchingPolicy(
                   maximumBatchingTimeSpan:TimeSpan.TryParse(batchingPolicy.MaximumBatchingTimeSpan, out var timeSpan)? timeSpan:TimeSpan.FromSeconds(10),
                   maximumNumberOfItems: batchingPolicy.MaximumNumberOfItems,
                   maximumRawDataSizeMB: batchingPolicy.MaximumRawDataSizeMB
               )
           );
            // Tip 1: Though most users should be fine with the defaults, to speed up ingestion, such as during development and in this sample app, we opt to modify the default ingestion policy to ingest data after at most 10 seconds.
            // Tip 2: This is generally a one-time configuration.
            // Tip 3: You can also skip the batching for some files using the Flush-Immediately property, though this option should be used with care as it is inefficient.
            await Utils.Queries.ExecuteAsync(adminClient, configDatabaseName, command);
            // If it failed to alter the ingestion policy - it could be the result of insufficient permissions. The sample will still run, though ingestion will be delayed for up to 5 minutes.
        }

        /// <summary>
        /// Queries the data on the existing number of rows.
        /// </summary>
        /// <param name="queryClient">Client to run queries</param>
        /// <param name="configDatabaseName">DB name</param>
        /// <param name="configTableName">Table name</param>
        private static async Task QueryExistingNumberOfRowsAsync(ICslQueryProvider queryClient, string configDatabaseName, string configTableName)
        {
            var query = $"{configTableName} | count";
            await Utils.Queries.ExecuteAsync(queryClient, configDatabaseName, query);
        }



        /// <summary>
        /// Second phase - The ingestion process.
        /// </summary>
        /// <param name="config">ConfigJson object</param>
        /// <param name="adminClient">Privileged client to run Control Commands</param>
        /// <param name="ingestClient">Client to ingest data</param>
        private static async Task IngestionAsync(ConfigJson config, ICslAdminProvider adminClient, IKustoIngestClient ingestClient, DirectoryInfo directory)
        {
            var files = directory.EnumerateFiles().ToList();
            foreach (var file in files)
            {
                AnsiConsole.WriteLine();
                config.Data.DataSourceUri = file.FullName;
                // Tip: This is generally a one-time configuration. Learn More: For more information about providing inline mappings and mapping references,
                // see: https://docs.microsoft.com/azure/data-explorer/kusto/management/mappings
                await CreateIngestionMappingsAsync(config.Data.UseExistingMapping, adminClient, config.DatabaseName, config.TableName, config.Data.MappingName, config.Data.MappingValue, config.Data.Format);

                // Learn More: For more information about ingesting data to Kusto in C#,
                // see: https://docs.microsoft.com/en-us/azure/data-explorer/net-sdk-ingest-data
                await IngestDataAsync(config.Data, config.Data.Format, ingestClient, config.DatabaseName, config.TableName, config.Data.MappingName);


                await Utils.Ingestion.WaitForIngestionToCompleteAsync(config.WaitForIngestSeconds);
                file.Delete();              
            }
        }

        /// <summary>
        /// Creates Ingestion Mappings (if required) based on given values.
        /// </summary>
        /// <param name="useExistingMapping">Flag noting if we should the existing mapping or create a new one</param>
        /// <param name="adminClient">Privileged client to run Control Commands</param>
        /// <param name="configDatabaseName">DB name</param>
        /// <param name="configTableName">Table name</param>
        /// <param name="mappingName">Desired mapping name</param>
        /// <param name="mappingValue">Values of the new mappings to create</param>
        /// <param name="dataFormat">Given data format</param>
        /// <returns>True if Ingestion Mappings exists (whether by us, or the already existing one)</returns>
        private static async Task CreateIngestionMappingsAsync(bool useExistingMapping, ICslAdminProvider adminClient, string configDatabaseName, string configTableName, string mappingName, string mappingValue, DataSourceFormat dataFormat)
        {
            if (useExistingMapping || mappingValue is null)
            {
                return;
            }

            var ingestionMappingKind = dataFormat.ToIngestionMappingKind().ToString().ToLower();
            WaitForUserToProceed($"Create a '{ingestionMappingKind}' mapping reference named '{mappingName}'");

            mappingName = mappingName ?? "DefaultQuickstartMapping" + Guid.NewGuid().ToString().Substring(0, 5);
            var mappingCommand = $".create-or-alter table {configTableName} ingestion {ingestionMappingKind} mapping '{mappingName}' '{mappingValue}'";

            await Utils.Queries.ExecuteAsync(adminClient, configDatabaseName, mappingCommand);

        }

        /// <summary>
        /// Ingest data from given source.
        /// </summary>
        /// <param name="dataSource">Given data source</param>
        /// <param name="dataFormat">Given data format</param>
        /// <param name="ingestClient">Client to ingest data</param>
        /// <param name="configDatabaseName">DB name</param>
        /// <param name="configTableName">Table name</param>
        /// <param name="mappingName">Desired mapping name</param>
        private static async Task IngestDataAsync(ConfigData dataSource, DataSourceFormat dataFormat, IKustoIngestClient ingestClient, string configDatabaseName, string configTableName, string mappingName)
        {
            var sourceType = dataSource.SourceType;
            var sourceUri = dataSource.DataSourceUri;
            WaitForUserToProceed($"Ingest '{sourceUri}' from '{sourceType}'");

            // Tip: When ingesting json files, if each line represents a single-line json, use MULTIJSON format even if the file only contains one line.
            // If the json contains whitespace formatting, use SINGLEJSON. In this case, only one data row json object is allowed per file.
            dataFormat = dataFormat == DataSourceFormat.json ? DataSourceFormat.multijson : dataFormat;

            // Tip: Kusto's C# SDK can ingest data from files, blobs and open streams.See the SDK's samples and the E2E tests in azure.kusto.ingest for additional references.
            // Note: No need to add "nosource" option as in that case the "ingestData" flag will be set to false, and it will be imppossible to reach this code segemnt.
            switch (sourceType)
            {
                case SourceType.localfilesource:
                    await Utils.Ingestion.IngestAsync(ingestClient, configDatabaseName, configTableName, sourceUri, dataFormat, mappingName, true);
                    break;
                case SourceType.blobsource:
                    await Utils.Ingestion.IngestAsync(ingestClient, configDatabaseName, configTableName, sourceUri, dataFormat, mappingName);
                    break;
                default:
                    Utils.ErrorHandler($"Unknown source '{sourceType}' for file '{sourceUri}'");
                    break;
            }
        }

        /// <summary>
        /// Third and final phase - simple queries to validate the hopefully successful run of the script.  
        /// </summary>
        /// <param name="queryProvider">Client to run queries</param>
        /// <param name="configDatabaseName">DB Name</param>
        /// <param name="configTableName">Table Name</param>
        /// <param name="configIngestData">Flag noting whether any data was ingested by the script</param>
        private static async Task PostIngestionQueryingAsync(ICslQueryProvider queryProvider, string configDatabaseName, string configTableName, bool configIngestData)
        {
            var optionalPostIngestionPrompt = configIngestData ? "post-ingestion " : "";

            WaitForUserToProceed($"Get {optionalPostIngestionPrompt}row count for '{configDatabaseName}.{configTableName}':");
            await QueryExistingNumberOfRowsAsync(queryProvider, configDatabaseName, configTableName);
        }

        /// <summary>
        /// Handles UX on prompts and flow of program 
        /// </summary>
        /// <param name="promptMsg"> Prompt to display to user.</param>
        private static void WaitForUserToProceed(string promptMsg)
        {
            AnsiConsole.WriteLine($"Step {Step}: {promptMsg}\n");
            Step++;
            if (WaitForUser)
            {
                AnsiConsole.WriteLine("Press any key to proceed with this operation...\n");
                Console.ReadLine();
            }
        }

    }
}
