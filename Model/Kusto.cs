using Kusto.Data.Common;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;

namespace BenchamarkHelper.Model
{
    /// <summary>
    /// SourceType - represents the type of files used for ingestion
    /// </summary>
    public enum SourceType
    {
        /// Ingest from local file
        localfilesource,
        /// Ingest from blob
        blobsource,
        /// Do not ingest (ingest from nowhere)
        nosource
    }

    /// <summary>
    /// AuthenticationModeOptions - represents the different options to autenticate to the system
    /// </summary>
    public enum AuthenticationModeOptions
    {
        /// Prompt user for credentials
        UserPrompt,
        /// Authenticate using a System-Assigned managed identity provided to an azure service, or using a User-Assigned managed identity.
        ManagedIdentity,
        /// Authenticate using an AAD Application
        AppKey,
        /// Authenticate using a certificate file.
        AppCertificate
    }
    /// <summary>
    /// ConfigData object - represents a file from which to ingest
    /// </summary>
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class ConfigData
    {
        /// SourceType Object
        public SourceType SourceType { get; set; }
        /// URI of source
        public string DataSourceUri { get; set; }
        /// Data's format (csv,JSON, etc.)
        public DataSourceFormat Format { get; set; }
        ///Flag to indicate whether to use an existing ingestion mapping, or to create a new one. 
        public bool UseExistingMapping { get; set; }
        /// Ingestion mapping name
        public string MappingName { get; set; }
        /// Ingestion mapping value
        public string MappingValue { get; set; }
    }

    /// <summary>
    /// ConfigJson object - represents a cluster and DataBase connection configuration file.
    /// </summary>
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class ConfigJson
    {
        /// Flag to indicate whether to use an existing table, or to create a new one. 
        public bool UseExistingTable { get; set; }

        /// DB to work on from the given URI.
        public string DatabaseName { get; set; }

        /// Table to work on from the given DB.
        public string TableName { get; set; }

        /// Table to work with from the given Table.
        public string TableSchema { get; set; }

        /// Cluster to connect to and query from.
        public string KustoUri { get; set; }

        /// Ingestion cluster to connect and ingest to. will usually be the same as the KustoUri, but starting with "ingest-"...
        public string IngestUri { get; set; }

        /// Tenant Id when using AppCertificate authentication mode.
        public string TenantId { get; set; }

        /// Data sources list to ingest from.
        public ConfigData Data { get; set; }

        /// Flag to indicate whether to Alter-merge the table (query).
        public bool AlterTable { get; set; }

        /// Flag to indicate whether to query the starting data (query).
        public bool QueryData { get; set; }

        /// Flag to indicate whether ingest data based on data sources.
        public bool IngestData { get; set; }

        /// Recommended default: UserPrompt
        /// Some of the auth modes require additional environment variables to be set in order to work (see usage in generate_connection_string function).
        /// Managed Identity Authentication only works when running as an Azure service (webapp, function, etc.)
        public AuthenticationModeOptions AuthenticationMode { get; set; }

        /// Recommended default: True
        /// Toggle to False to execute this script "unattended"
        public bool WaitForUser { get; set; }

        /// Sleep time to allow for queued ingestion to complete.
        public int WaitForIngestSeconds { get; set; }

        /// Optional - Customized ingestion batching policy
        public BatchingPolicy BatchingPolicy { get; set; }
    }


    public class BatchingPolicy
    {
        public string MaximumBatchingTimeSpan { get; set; }
        public int MaximumNumberOfItems { get; set; }

        public int MaximumRawDataSizeMB { get; set; }
    }
}