// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// The <see cref="IContentTracker"/> is responsible for tracking the location of content in the system. That is, it
/// knows which machines have which pieces of content.
/// </summary>
public interface IContentTracker : IStartupShutdownSlim
{
    /// <summary>
    /// Update the content tracker with additional information about the location of content.
    /// </summary>
    public Task<BoolResult> UpdateLocationsAsync(OperationContext context, UpdateLocationsRequest request);

    /// <summary>
    /// Obtain information about the location of content.
    /// </summary>
    public Task<Result<GetLocationsResponse>> GetLocationsAsync(OperationContext context, GetLocationsRequest request);
}

public static class ContentTrackerExtensions
{
    public static async Task<Result<ContentEntry>> GetSingleLocationAsync(this IContentTracker contentTracker, OperationContext context, ShortHash hash)
    {
        var result = await contentTracker.GetLocationsAsync(context, new GetLocationsRequest() { Hashes = new[] { hash }, });
        return result.Select(v => v.Results.First());
    }
}

/// <summary>
/// The <see cref="ILocalContentTracker"/> represents an <see cref="IContentTracker"/> that is local to the machine
/// it's running on (i.e., it does not communicate with other machines).
/// </summary>
public interface ILocalContentTracker : IContentTracker
{
    /// <summary>
    /// Each <see cref="ChangeStamp"/> has a sequence number, which is used to determine the order in which operations
    /// happened to be able to order them. When a machine is performing an operation, the <see cref="ChangeStamp"/>
    /// associated with the operation is created by the machine itself. This method returns the highest sequence number
    /// that we have observed for a given machine.
    /// </summary>
    /// <remarks>
    /// A priori, the sequence number can be defined per (Machine, Hash) basis (i.e., each hash in each machine starts
    /// at 0 and is increased every time). In any case, it is extremely important that the sequence number be increased
    /// every time an operation is performed, and that it is never decreased.
    /// </remarks>
    public SequenceNumber GetSequenceNumber(ShortHash shortHash, MachineId machineId);
}

/// <summary>
/// The <see cref="IDistributedContentTracker"/> represents an <see cref="IContentTracker"/> that communicates with
/// other machines as necessary to power a datacenter-scale cache.
/// </summary>
/// <remarks>
/// This interface mainly exists for testing.
/// </remarks>
public interface IDistributedContentTracker : IContentTracker
{
    public Task ProcessLocalChangeAsync(Context tracingContext, ChangeStampOperation operation, ContentHashWithSize contentHashWithSize);
}

[ProtoContract]
public record GetLocationsResponse
{
    [ProtoMember(1)]
    public IReadOnlyList<ContentEntry> Results { get; init; } = new List<ContentEntry>();

    public override string ToString()
    {
        var entries = string.Join("; ", Results.Select(h => h.ToString()));
        return $"{nameof(GetLocationsResponse)} {{ {entries} }}";
    }
}

[ProtoContract]
public record GetLocationsRequest
{
    [ProtoMember(1)]
    public IReadOnlyList<ShortHash> Hashes { get; init; } = new List<ShortHash>();

    public override string ToString()
    {
        var entries = string.Join("; ", Hashes.Select(h => h.ToString()));
        return $"{nameof(GetLocationsRequest)} {{ {entries} }}";
    }
}

[ProtoContract]
public record UpdateLocationsRequest
{
    [ProtoMember(1)]
    public IReadOnlyList<ContentEntry> Entries { get; init; } = new List<ContentEntry>();

    public override string ToString()
    {
        var entries = string.Join("; ", Entries.Select(h => h.ToString()));
        return $"{nameof(UpdateLocationsRequest)} {{ {entries} }}";
    }
}

[ProtoContract]
public record ContentEntry
{
    [ProtoMember(1)]
    public ShortHash Hash { get; init; }

    [ProtoMember(2)]
    public long Size { get; init; } = -1;

    [ProtoMember(3)]
    public IReadOnlyList<Stamped<MachineId>> Operations { get; init; } = new List<Stamped<MachineId>>();

    public override string ToString()
    {
        var operations = string.Join(", ", Operations.Select(o => $"{o.Value}({o.ChangeStamp})"));
        return $"{Hash}:{Size}[{operations}]";
    }

    public IEnumerable<MachineId> Existing()
    {
        return Select(ChangeStampOperation.Add);
    }

    public IEnumerable<MachineId> Tombstones()
    {
        return Select(ChangeStampOperation.Delete);
    }

    private IEnumerable<MachineId> Select(ChangeStampOperation operation)
    {
        return Operations.Where(stamped => stamped.ChangeStamp.Operation == operation).Select(stamped => stamped.Value);
    }

    public bool Contains(MachineId machineId)
    {
        return HasOperation(machineId, ChangeStampOperation.Add);
    }

    public bool Tombstone(MachineId machineId)
    {
        return HasOperation(machineId, ChangeStampOperation.Delete);
    }

    private bool HasOperation(MachineId machineId, ChangeStampOperation operation)
    {
        return Operations.Any(o => o.Value == machineId && o.ChangeStamp.Operation == operation);
    }
}
