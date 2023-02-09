﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Kusto.Cloud.Platform.Utils;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using Kusto.Ingest;
using Microsoft.Azure.WebJobs.Extensions.Kusto.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Extensions.Kusto
{
    /// <summary>
    /// Wrap around Kusto internal classes and provides a mechanism to provide the query and ingest clients. Has an additional benefit that
    /// testing is a lot easier
    /// </summary>
    internal interface IKustoClientFactory
    {
        IKustoIngestClient IngestClientFactory(string engineConnectionString, string managedIdentity, ILogger logger);
        ICslQueryProvider QueryProviderFactory(string engineConnectionString, string managedIdentity, ILogger logger);
    }
    internal class KustoClient : IKustoClientFactory
    {
        /// <summary>
        /// Given the engine connection string, return a managed ingest client
        /// </summary>
        /// <param name="engineConnectionString">The engine connection string. The ingest URL will be derieved from this for the managed ingest</param>
        /// <param name="managedIdentity">MSI string to use Managed service identity</param>
        /// <returns>A managed ingest client. Attempts ingestion through streaming and then fallsback to Queued ingest mode</returns>
        public IKustoIngestClient IngestClientFactory(string engineConnectionString, string managedIdentity, ILogger logger)
        {
            KustoConnectionStringBuilder engineKcsb = GetKustoConnectionString(engineConnectionString, managedIdentity);
            /*
                We expect minimal input from the user.The end user can just pass a connection string, we need to decipher the DM
                ingest endpoint as well from this. Both the engine and DM endpoint are needed for the managed ingest to happen
             */
            string dmConnectionStringEndpoint = engineKcsb.Hostname.Contains(KustoConstants.IngestPrefix) ? engineConnectionString : engineConnectionString.ReplaceFirstOccurrence(KustoConstants.ProtocolSuffix, KustoConstants.ProtocolSuffix + KustoConstants.IngestPrefix);
            KustoConnectionStringBuilder dmKcsb = GetKustoConnectionString(dmConnectionStringEndpoint, managedIdentity);
            // Measure the time it takes for a connection
            var ingestClientInitialize = new Stopwatch();
            ingestClientInitialize.Start();
            // Create a managed ingest connection , needed to debug connection issues
            IKustoIngestClient managedIngestClient = GetManagedStreamingClient(engineKcsb, dmKcsb);
            ingestClientInitialize.Stop();
            logger.LogDebug($"Initializing ingest client with the connection string : {KustoBindingUtils.ToSecureString(engineConnectionString)}  took {ingestClientInitialize.ElapsedMilliseconds} milliseconds");
            return managedIngestClient;
        }
        private static IKustoIngestClient GetManagedStreamingClient(KustoConnectionStringBuilder engineKcsb, KustoConnectionStringBuilder dmKcsb)
        {
            return KustoIngestFactory.CreateManagedStreamingIngestClient(engineKcsb, dmKcsb);
        }

        /// <summary>
        /// Given the engine connection string, return a query client
        /// </summary>
        /// <param name="engineConnectionString">The engine connection string</param>
        /// <param name="managedIdentity">MSI string to use Managed service identity</param>
        /// <returns>A query client to execute KQL</returns>

        public ICslQueryProvider QueryProviderFactory(string engineConnectionString, string managedIdentity, ILogger logger)
        {
            KustoConnectionStringBuilder engineKcsb = GetKustoConnectionString(engineConnectionString, managedIdentity);
            var timer = new Stopwatch();
            timer.Start();
            // Create a query client connection. This is needed in cases to debug any connection issues
            ICslQueryProvider queryProvider = KustoClientFactory.CreateCslQueryProvider(engineKcsb);
            timer.Stop();
            logger.LogDebug($"Initializing query client with the connection string : {KustoBindingUtils.ToSecureString(engineConnectionString)}  took {timer.ElapsedMilliseconds} milliseconds");
            return queryProvider;
        }

        private static KustoConnectionStringBuilder GetKustoConnectionString(string connectionString, string managedIdentity)
        {
            var kcsb = new KustoConnectionStringBuilder(connectionString)
            {
                ClientVersionForTracing = KustoConstants.ClientDetailForTracing
            };
            if (!string.IsNullOrEmpty(managedIdentity))
            {
                // There exists a managed identity. Check if that is UserManaged or System identity
                // use "system" to indicate the system-assigned identity
                if ("system".EqualsOrdinalIgnoreCase(managedIdentity))
                {
                    kcsb.WithAadSystemManagedIdentity();
                }
                else
                {
                    kcsb.WithAadUserManagedIdentity(managedIdentity);
                }
            }
            return kcsb;
        }
    }
}
