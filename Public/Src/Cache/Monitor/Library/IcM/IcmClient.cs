﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.MemoizationStore.Interfaces;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using Microsoft.AzureAd.Icm.Types;
using Microsoft.AzureAd.Icm.WebService.Client;

namespace BuildXL.Cache.Monitor.Library.IcM
{
    public interface IIcmClient
    {
        Task EmitIncidentAsync(IcmIncident incident);
    }

    public class IcmClient : IIcmClient
    {
        private readonly KeyVaultClient _keyVault;
        private readonly string _uri;
        private readonly Guid _connectorId;
        private readonly string _connectorCertificateName;
        private readonly VolatileSet<string> _cachedIcms;

        public IcmClient(KeyVaultClient keyVault, string icmUri, Guid connectorId, string connectorCertificateName, IClock clock)
        {
            _keyVault = keyVault;
            _uri = icmUri;
            _connectorId = connectorId;
            _connectorCertificateName = connectorCertificateName;
            _cachedIcms = new VolatileSet<string>(clock);
        }

        public async Task EmitIncidentAsync(IcmIncident incident)
        {
            if (incident.CacheTimeToLive is not null)
            {
                if (_cachedIcms.Contains(incident.Title))
                {
                    // Update TTL and return.
                    _cachedIcms.Add(incident.Title, incident.CacheTimeToLive.Value);
                    return;
                }
                else
                {
                    // Add to cached incidents.
                    _cachedIcms.Add(incident.Title, incident.CacheTimeToLive.Value);
                }
            }
                
            var cert = await _keyVault.GetCertificateAsync(_connectorCertificateName);
            var incidentToSend = GetIncidentToSend(incident);

            using var client = ConnectorClientFactory.CreateClient(_uri, cert);
            try
            {
                var result = client.AddOrUpdateIncident2(_connectorId, incidentToSend, RoutingOptions.None);
                if (result.Status != IncidentAddUpdateStatus.AddedNew &&
                    // Discarded means that we're updating hit count or it was suppressed because it's too soon
                    // since we updated hit count last and the incident is still active.
                    result.Status != IncidentAddUpdateStatus.Discarded)
                {
                    throw new Exception($"Result status does not indicate success: {result.Status}.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to submit incident to IcM:\n" + e.ToString());
                throw;
            }
        }

        /// <summary>Generates the incident object</summary>
        /// <returns>resulting value</returns>
        private AlertSourceIncident GetIncidentToSend(IcmIncident incident)
        {
            DateTime now = incident.IncidentTime ?? DateTime.UtcNow;

            var description =
                $"{incident.Description}" +
                (incident.Machines is null ? string.Empty : $"\n\nMachines: {string.Join(", ", incident.Machines!)}\n") +
                (incident.CorrelationIds is null ? string.Empty : $"\n\nCorrelation IDs: {string.Join(", ", incident.CorrelationIds!)}");

            return new AlertSourceIncident
            {
                Source = new AlertSourceInfo
                {
                    CreatedBy = "tsecache@microsoft.com",
                    Origin = "Monitor",
                    CreateDate = now,
                    ModifiedDate = now,
                    IncidentId = Guid.NewGuid().ToString("N"),
                },
                RoutingId = "CloudBuild/DRI/Cache",
                OwningTeamId = "DRI - CloudBuild - Cache",
                OccurringLocation = new IncidentLocation
                {
                    DataCenter = incident.Stamp,
                    DeviceName = incident.Machines?.FirstOrDefault() ?? "Cache Monitor",
                    Environment = incident.Environment,
                },
                RaisingLocation = new IncidentLocation
                {
                    DeviceName = "CacheMonitor",
                },
                Severity = incident.Severity,
                Status = IncidentStatus.Active,
                DescriptionEntries = new[]
                {
                    new DescriptionEntry
                    {
                        Cause = DescriptionEntryCause.Created,
                        Date = now,
                        ChangedBy = "Cache Monitor",
                        SubmitDate = now,
                        SubmittedBy = "Cache Monitor",
                        RenderType = DescriptionTextRenderType.Plaintext,
                        Text = description,
                    }
                },
                Title = incident.Title,
                CorrelationId = incident.Title, // For the purposes of the monitor, if an incident has the same title, it's a repeat.
            };
        }
    }

    /// <nodoc />
    public class IcmIncident
    {
        /// <nodoc />
        public string Stamp { get; set; }

        public string Environment { get; set; }

        /// <nodoc />
        public IEnumerable<string>? Machines { get; set; }

        public IEnumerable<string>? CorrelationIds { get; set; }

        /// <nodoc />
        public int Severity { get; set; }

        /// <nodoc />
        public string Description { get; set; }

        /// <nodoc />
        public string Title { get; set; }

        public DateTime? IncidentTime { get; set; }

        /// <summary>
        /// If null, this does nothing.
        /// When this has a value, the client will remember the incident by its title and make sure that we don't
        /// emit a new incident while the original TTL is still valid.
        /// </summary>
        public TimeSpan? CacheTimeToLive { get; set; }

        /// <nodoc />
        public IcmIncident(
            string stamp,
            string environment,
            IEnumerable<string>? machines,
            IEnumerable<string>? correlationIds,
            int severity,
            string description,
            string title,
            DateTime? incidentTime,
            TimeSpan? cacheTimeToLive)
        {
            Stamp = stamp;
            Environment = environment;
            Machines = machines;
            CorrelationIds = correlationIds;
            Severity = severity;
            Description = description;
            Title = title;
            IncidentTime = incidentTime;
            CacheTimeToLive = cacheTimeToLive;
        }
    }
}
