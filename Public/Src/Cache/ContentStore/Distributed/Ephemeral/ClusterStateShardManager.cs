﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// This class is responsible for bridging the gap between <see cref="ClusterState"/> and
/// <see cref="IShardManager{TLoc}"/>. This allows us to use <see cref="ClusterState"/> as a source of truth for the
/// shards available in the cluster at any given time and transparently handle resharding in any sharding scheme
/// (<see cref="IShardingScheme{TKey,TLoc}"/>) that uses <see cref="IShardManager{TLoc}"/>.
/// </summary>
public class ClusterStateShardManager : IShardManager<MachineId>
{
    /// <inheritdoc />
    public IReadOnlyList<ILocation<MachineId>> Locations { get; private set; }

    /// <inheritdoc />
    public event EventHandler? OnResharding;

    public ClusterStateShardManager(ClusterState clusterState)
    {
        Locations = ProcessClusterStateUpdate(clusterState.QueryableClusterState);

        clusterState.OnClusterStateUpdate += OnClusterStateUpdate;
    }

    private void OnClusterStateUpdate(OperationContext context, QueryableClusterState clusterState)
    {
        Locations = ProcessClusterStateUpdate(clusterState);
        OnResharding?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<ILocation<MachineId>> ProcessClusterStateUpdate(QueryableClusterState clusterState)
    {
        var locations = new List<ILocation<MachineId>>();
        foreach (var entry in clusterState.RecordsByMachineId)
        {
            var id = entry.Key;
            var record = entry.Value;
            locations.Add(new Entry(id, record.IsOpen() || record.IsClosed()));
        }
        return locations;
    }

    private class Entry : ILocation<MachineId>
    {
        public MachineId Location { get; }

        public bool Available { get; }

        public Entry(MachineId location, bool available)
        {
            Location = location;
            Available = available;
        }
    }
}
