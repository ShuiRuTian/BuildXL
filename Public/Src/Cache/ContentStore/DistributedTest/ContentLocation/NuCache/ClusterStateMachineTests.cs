﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class ClusterStateMachineTests
    {
        private readonly MemoryClock _clock = new MemoryClock();

        // WARNING: DO NOT DISABLE THIS TEST. READ BELOW.
        [Fact]
        public void MachineIdSerialization()
        {
            // This test is testing that serialization for: ClusterStateMachine, MachineRecord, and MachineId is
            // entirely backwards-compatible. If it isn't, a change can break a stamp by breaking either ClusterState,
            // RocksDbContentLocationDatabase, or both of them, and completely obliterate the cluster.

            var clusterState = new ClusterStateMachine();
            (clusterState, _) = clusterState.RegisterMachine(MachineLocation.Create("node", 1234), DateTime.MinValue);

            var str = JsonUtilities.JsonSerialize(clusterState, indent: false);
            str.Should().BeEquivalentTo(@"{""NextMachineId"":2,""Records"":[{""Id"":1,""Location"":""grpc://node:1234/"",""State"":""Open"",""LastHeartbeatTimeUtc"":""0001-01-01T00:00:00""}]}");

            var deserialized = JsonUtilities.JsonDeserialize<ClusterStateMachine>(str);
            deserialized.NextMachineId.Should().Be(clusterState.NextMachineId);
            deserialized.Records.Count.Should().Be(1);
        }

        [Fact]
        public void RegisterNewMachineUsesCorrectDefaults()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;
            MachineId machineId;

            var machineLocation = new MachineLocation(@"\\node1\dir");

            (clusterState, machineId) = clusterState.RegisterMachine(machineLocation, nowUtc);
            machineId.Index.Should().Be(MachineId.MinValue);

            var record = clusterState.GetRecord(machineId).ThrowIfFailure();
            record.Should().BeEquivalentTo(
                new MachineRecord()
                {
                    Id = new MachineId(MachineId.MinValue),
                    Location = machineLocation,
                    State = ClusterStateMachine.InitialState,
                    LastHeartbeatTimeUtc = nowUtc,
                });
        }

        [Fact]
        public void RegisterMachineEmitsIdsInSequence()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;
            MachineId machineId;

            (clusterState, machineId) = clusterState.RegisterMachine(new MachineLocation("node1"), nowUtc);
            machineId.Index.Should().Be(1);

            (_, machineId) = clusterState.RegisterMachine(new MachineLocation("node2"), nowUtc);
            machineId.Index.Should().Be(2);
        }

        [Fact]
        public void RegisterMachineTransitionTests()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;

            // During transition, all machines will be added forcefully without regard for the consistency of the data
            // structure
            clusterState = clusterState.ForceRegisterMachine(new MachineId(3), MachineLocation.Create("A", 0), nowUtc);
            clusterState.NextMachineId.Should().Be(4);

            clusterState = clusterState.ForceRegisterMachine(new MachineId(8), MachineLocation.Create("B", 0), nowUtc);
            clusterState.NextMachineId.Should().Be(9);

            clusterState = clusterState.ForceRegisterMachine(new MachineId(16), MachineLocation.Create("C", 0), nowUtc);
            clusterState.NextMachineId.Should().Be(17);

            clusterState = clusterState.ForceRegisterMachine(new MachineId(20), MachineLocation.Create("D", 0), nowUtc);
            clusterState.NextMachineId.Should().Be(21);

            clusterState = clusterState.ForceRegisterMachine(new MachineId(23), MachineLocation.Create("D", 0), nowUtc);
            clusterState.NextMachineId.Should().Be(24);

            // After transition, adding proceeds as usual, by appending to the end basically
            MachineId n1Id;
            (clusterState, n1Id) = clusterState.RegisterMachine(MachineLocation.Create("Machine Gets Added After Transition", 0), nowUtc);
            n1Id.Index.Should().Be(24);
            clusterState.NextMachineId.Should().Be(25);
        }

        [Fact]
        public void HeartbeatUpdatesLastHeartbeatTimeAndState()
        {
            var clusterState = new ClusterStateMachine();
            MachineId machineId;

            (clusterState, machineId) = clusterState.RegisterMachine(new MachineLocation("node1"), _clock.UtcNow);

            _clock.Increment(TimeSpan.FromMinutes(1));
            clusterState = clusterState.Heartbeat(machineId, _clock.UtcNow, MachineState.Open).ThrowIfFailure().Next;

            var r = clusterState.GetRecord(machineId).ShouldBeSuccess().Value;
            r.LastHeartbeatTimeUtc.Should().Be(_clock.UtcNow);
            r.State.Should().Be(MachineState.Open);
        }

        [Fact]
        public void HeartbeatKeepsOtherRecordsAsIs()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;

            MachineId n1Id;
            (clusterState, n1Id) = clusterState.RegisterMachine(new MachineLocation("node1"), nowUtc);

            MachineId n2Id;
            (clusterState, n2Id) = clusterState.RegisterMachine(new MachineLocation("node2"), nowUtc);

            _clock.Increment(TimeSpan.FromMinutes(1));
            clusterState = clusterState.Heartbeat(n1Id, _clock.UtcNow, MachineState.Closed).ThrowIfFailure().Next;

            var r = clusterState.GetRecord(n2Id).ShouldBeSuccess().Value;
            r.LastHeartbeatTimeUtc.Should().Be(nowUtc);
            r.State.Should().Be(MachineState.Open);
        }

        [Fact]
        public void RecomputeChangesStatesAsExpected()
        {
            var clusterState = new ClusterStateMachine();
            var cfg = new ClusterStateRecomputeConfiguration();

            var nowUtc = _clock.UtcNow;

            // We want to test all possible state machine transitions. To do so, we generate a very specific instance
            // of cluster state meant to transition the way we expect instead of actually simulating each possible
            // branch.

            var n1 = MachineLocation.Create("node2", 0);
            MachineId n1Id;
            (clusterState, n1Id) = clusterState.ForceRegisterMachineWithState(n1, nowUtc, MachineState.DeadUnavailable);

            var n2 = MachineLocation.Create("node3", 0);
            MachineId n2Id;
            (clusterState, n2Id) = clusterState.ForceRegisterMachineWithState(n2, nowUtc, MachineState.DeadExpired);

            var n3 = MachineLocation.Create("node4", 0);
            MachineId n3Id;
            (clusterState, n3Id) = clusterState.ForceRegisterMachineWithState(n3, nowUtc - cfg.ActiveToExpired, MachineState.Open);

            var n4 = MachineLocation.Create("node5", 0);
            MachineId n4Id;
            (clusterState, n4Id) = clusterState.ForceRegisterMachineWithState(n4, nowUtc - cfg.ActiveToClosed, MachineState.Open);

            var n5 = MachineLocation.Create("node6", 0);
            MachineId n5Id;
            (clusterState, n5Id) = clusterState.ForceRegisterMachineWithState(n5, nowUtc, MachineState.Open);

            var n6 = MachineLocation.Create("node7", 0);
            MachineId n6Id;
            (clusterState, n6Id) = clusterState.ForceRegisterMachineWithState(n6, nowUtc - cfg.ClosedToExpired, MachineState.Closed);

            var n7 = MachineLocation.Create("node8", 0);
            MachineId n7Id;
            (clusterState, n7Id) = clusterState.ForceRegisterMachineWithState(n7, nowUtc, MachineState.Closed);

            clusterState = clusterState.TransitionInactiveMachines(cfg, nowUtc);

            clusterState.GetRecord(n1Id).ThrowIfFailure().State.Should().Be(MachineState.DeadUnavailable);
            clusterState.GetRecord(n2Id).ThrowIfFailure().State.Should().Be(MachineState.DeadExpired);
            clusterState.GetRecord(n3Id).ThrowIfFailure().State.Should().Be(MachineState.DeadExpired);
            clusterState.GetRecord(n4Id).ThrowIfFailure().State.Should().Be(MachineState.Closed);
            clusterState.GetRecord(n5Id).ThrowIfFailure().State.Should().Be(MachineState.Open);
            clusterState.GetRecord(n6Id).ThrowIfFailure().State.Should().Be(MachineState.DeadExpired);
            clusterState.GetRecord(n7Id).ThrowIfFailure().State.Should().Be(MachineState.Closed);
        }


        [Fact]
        public void InactiveMachineIdsAreReclaimed()
        {
            var clusterState = new ClusterStateMachine();
            var cfg = new ClusterStateRecomputeConfiguration();

            var n1 = MachineLocation.Create("node1", 0);

            (clusterState, var ids) = clusterState.RegisterMany(cfg, new IClusterStateStorage.RegisterMachineInput(new[] { n1 }), _clock.UtcNow);
            var n1Id = ids[0];

            _clock.Increment(cfg.ActiveToUnavailable + TimeSpan.FromHours(1));

            var n2 = MachineLocation.Create("node2", 0);
            (clusterState, ids) = clusterState.RegisterMany(cfg, new IClusterStateStorage.RegisterMachineInput(new[] { n2 }), _clock.UtcNow);
            var n2Id = ids[0];

            n2Id.Index.Should().Be(2, "The machine ID should be 2 because the registration shouldn't have been allowed to take over IDs");

            var n3 = MachineLocation.Create("node3", 0);
            (clusterState, ids) = clusterState.RegisterMany(cfg, new IClusterStateStorage.RegisterMachineInput(new[] { n3 }), _clock.UtcNow);
            var n3Id = ids[0];

            clusterState.GetRecord(n3Id).ThrowIfFailure().Location.Should().Be(n3);
            n3Id.Index.Should().Be(1, "The machine ID for node3 should be 1 because it should have taken over node1's due to inactivity");
        }
    }
}
