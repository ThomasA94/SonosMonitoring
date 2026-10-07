using System.Text.Json;
using SonosMonitoring.Application.Collection;

namespace SonosMonitoring.Unit.Tests.Collection;

/// <summary>Verifies privacy reduction and observation preservation for diagnostic snapshots.</summary>
public sealed class CollectorSnapshotRedactorTests
{
    /// <summary>Removes private canaries from every copied string field, including future error values.</summary>
    [Fact]
    public void RedactRemovesPrivateCanariesFromSerializedSnapshot()
    {
        CollectorSnapshot input = this.CreateSnapshot();

        CollectorSnapshot output = CollectorSnapshotRedactor.Redact(input);
        string json = JsonSerializer.Serialize(output);

        string[] canaries =
        [
            "SECRET_DEVICE_A", "SECRET_DEVICE_B", "SECRET_SATELLITE", "SECRET_PARENT",
            "192.168.77.21", "192.168.77.22", "192.168.77.23", "192.168.77.24",
            "SECRET_GROUP_ID", "SECRET_ROOM_A", "SECRET_ROOM_B", "SECRET_ROOM_SATELLITE", "SECRET_MODEL_A",
            "SECRET_MODEL_B", "SECRET_INTERFACE", "192.168.77.0/24", "SECRET_SOURCE_NAME", "SECRET_INTERFACE_SOURCE",
            "SECRET_DEVICE_ERROR", "SECRET_RANGE_ERROR", "SECRET_SSDP_ERROR"
        ];
        Assert.All(canaries, canary => Assert.DoesNotContain(canary, json, StringComparison.Ordinal));
        Assert.All(output.Devices, device => Assert.Null(device.ModelName));
        Assert.Equal("other_error", output.Devices[0].Description.ErrorCode);
        Assert.Equal("other_error", output.Ranges[0].ErrorCode);
        Assert.Equal("other_error", output.Ssdp.ErrorCode);
    }

    /// <summary>Reuses export-local aliases across reporters and references to unlisted members.</summary>
    [Fact]
    public void RedactPreservesReferenceIdentityAcrossReportersAndUnknownMembers()
    {
        CollectorSnapshot output = CollectorSnapshotRedactor.Redact(this.CreateSnapshot());
        DeviceSnapshot first = output.Devices[0];
        DeviceSnapshot second = output.Devices[1];
        TopologyGroup firstGroup = Assert.Single(first.Groups);
        TopologyGroup secondGroup = Assert.Single(second.Groups);
        TopologyMember satellite = Assert.Single(firstGroup.Members, member => member.IsInvisible == true);

        Assert.Equal(second.DeviceId, firstGroup.CoordinatorDeviceId);
        Assert.Equal(second.DeviceId, secondGroup.CoordinatorDeviceId);
        Assert.Equal("group-1", firstGroup.GroupId);
        Assert.Equal(firstGroup.GroupId, secondGroup.GroupId);
        Assert.Equal(first.DeviceId, Assert.Single(secondGroup.Members, member => member.DeviceId == first.DeviceId).DeviceId);
        Assert.Equal(first.DeviceId, satellite.ParentDeviceId);
        Assert.NotNull(satellite.RoomName);
        Assert.NotEqual(first.RoomName, satellite.RoomName);
        Assert.NotNull(satellite.ReportedAddress);
        Assert.NotEqual(first.Address, satellite.ReportedAddress);
        Assert.NotEqual(second.Address, satellite.ReportedAddress);
        Assert.Equal(satellite.DeviceId, secondGroup.Members[2].ParentDeviceId);
        Assert.NotEqual(first.DeviceId, secondGroup.Members[2].DeviceId);
        Assert.NotEqual(second.DeviceId, secondGroup.Members[2].DeviceId);
    }

    /// <summary>Preserves timestamps, quality, counters, caps, and null observations while detaching arrays.</summary>
    [Fact]
    public void RedactPreservesObservationsAndCopiesMutableCollections()
    {
        CollectorSnapshot input = this.CreateSnapshot();
        CollectorSnapshot output = CollectorSnapshotRedactor.Redact(input);
        DeviceSnapshot originalDevice = output.Devices[0];
        DateTimeOffset attempt = new(2026, 9, 29, 10, 11, 12, TimeSpan.Zero);

        Assert.Equal(input.HeartbeatUtc, output.HeartbeatUtc);
        Assert.Equal(input.LastSweepUtc, output.LastSweepUtc);
        Assert.True(output.Enabled);
        Assert.True(output.CapacityExceeded);
        Assert.Equal(input.Devices[0].FirstSeenUtc, originalDevice.FirstSeenUtc);
        Assert.Equal(input.Devices[0].LastSeenUtc, originalDevice.LastSeenUtc);
        Assert.Equal(attempt, originalDevice.Description.LastAttemptUtc);
        Assert.Equal(attempt.AddMinutes(-1), originalDevice.Description.LastSuccessUtc);
        Assert.False(originalDevice.Description.LastAttemptSucceeded);
        Assert.True(originalDevice.Description.IsStale);
        Assert.Equal("other_error", originalDevice.Description.ErrorCode);
        Assert.Equal("ifconfig", originalDevice.InterfaceStatus.Name);
        Assert.Equal("topology", originalDevice.TopologyStatus.Name);
        Assert.Null(originalDevice.Groups[0].Members[0].RoomName);
        Assert.Equal(101UL, originalDevice.Interfaces[0].ReceiveBytes);
        Assert.Equal(202UL, originalDevice.Interfaces[0].TransmitBytes);
        Assert.Equal(2, output.Ranges[0].AddressesProbed);
        Assert.Equal(1, output.Ranges[0].DevicesFound);
        Assert.Equal(attempt, output.Ranges[0].LastAttemptUtc);
        Assert.Equal(attempt, output.Ranges[0].LastCompletedUtc);
        Assert.False(output.Ranges[0].LastAttemptSucceeded);
        Assert.True(output.Ssdp.Enabled);
        Assert.Equal(attempt, output.Ssdp.LastAttemptUtc);
        Assert.Equal(attempt, output.Ssdp.LastCompletedUtc);
        Assert.Equal(1, output.Ssdp.FailedInterfaces);
        Assert.Equal(input.Ssdp.CandidateCount, output.Ssdp.CandidateCount);
        Assert.Equal(input.Ssdp.LimitReached, output.Ssdp.LimitReached);
        Assert.Equal(attempt, output.TopologyDiscovery.LastSelectionUtc);
        Assert.Equal(4, output.TopologyDiscovery.CandidateCount);
        Assert.True(output.TopologyDiscovery.LimitReached);

        ((InterfaceCounters[])input.Devices[0].Interfaces)[0] = new("MUTATED_INTERFACE", 0, 0, 0, 0, 0, 0, 0, 0);
        ((TopologyMember[])input.Devices[0].Groups[0].Members)[0] = new("MUTATED_MEMBER", null, null, null);
        ((TopologyGroup[])input.Devices[0].Groups)[0] = new("MUTATED_GROUP", "MUTATED_COORDINATOR", []);
        ((DiscoveryRangeSnapshot[])input.Ranges)[0] = new("MUTATED_CIDR", null, null, false, 0, 0, null);
        ((DeviceSnapshot[])input.Devices)[0] = CreateEmptyDevice("MUTATED_DEVICE");

        Assert.Equal(originalDevice.DeviceId, output.Devices[0].DeviceId);
        Assert.Equal(101UL, output.Devices[0].Interfaces[0].ReceiveBytes);
        Assert.Equal("group-1", output.Devices[0].Groups[0].GroupId);
        Assert.Equal("device-1", output.Devices[0].Groups[0].Members[0].DeviceId);
        Assert.Equal("range-1", output.Ranges[0].Cidr);
    }

    /// <summary>Starts fresh alias numbering for each call and emits a valid empty snapshot.</summary>
    [Fact]
    public void RedactUsesIndependentAliasesAndSupportsEmptySnapshots()
    {
        CollectorSnapshot input = this.CreateSnapshot();
        CollectorSnapshot first = CollectorSnapshotRedactor.Redact(input);
        DeviceSnapshot[] reversed = input.Devices.Reverse().ToArray();
        CollectorSnapshot reordered = input with { Devices = reversed };
        CollectorSnapshot second = CollectorSnapshotRedactor.Redact(reordered);
        CollectorSnapshot empty = CollectorSnapshotRedactor.Redact(new(false, null, null, [], [], false));

        Assert.Equal("device-1", first.Devices[0].DeviceId);
        Assert.Equal("device-1", second.Devices[0].DeviceId);
        Assert.NotEqual(first.Devices[1].DeviceId, second.Devices[0].DeviceId);
        Assert.Empty(empty.Devices);
        Assert.Empty(empty.Ranges);
        Assert.False(empty.Enabled);
        Assert.False(empty.CapacityExceeded);
    }

    /// <summary>Builds two reporters that share topology identities and include private canary values.</summary>
    /// <returns>A source snapshot with deliberately identifying values and mutable array collections.</returns>
    private CollectorSnapshot CreateSnapshot()
    {
        DateTimeOffset attempt = new(2026, 9, 29, 10, 11, 12, TimeSpan.Zero);
        SourceSnapshot badDescription = new("SECRET_SOURCE_NAME", false, attempt, attempt.AddMinutes(-1), true, "SECRET_DEVICE_ERROR");
        SourceSnapshot goodInterface = new("SECRET_INTERFACE_SOURCE", true, attempt, attempt, false, null);
        TopologyGroup firstGroup = new("SECRET_GROUP_ID", "SECRET_DEVICE_B",
        new TopologyMember[]
        {
            new("SECRET_DEVICE_A", null, false, null),
            new("SECRET_SATELLITE", "SECRET_ROOM_SATELLITE", true, "SECRET_DEVICE_A")
            {
                ReportedAddress = "192.168.77.23"
            }
        });
        TopologyGroup secondGroup = new("SECRET_GROUP_ID", "SECRET_DEVICE_B",
        [
            new("SECRET_DEVICE_A", "SECRET_ROOM_A", false, null),
            new("SECRET_DEVICE_B", "SECRET_ROOM_B", false, null),
            new("SECRET_PARENT", "SECRET_ROOM_B", null, "SECRET_SATELLITE")
            {
                ReportedAddress = "192.168.77.24"
            }
        ]);
        DeviceSnapshot first = new("SECRET_DEVICE_A", "192.168.77.21", "SECRET_ROOM_A", "SECRET_MODEL_A",
            attempt.AddHours(-1), attempt, badDescription, goodInterface,
            new InterfaceCounters[] { new("SECRET_INTERFACE", 101, 202, 3, 4, 5, 6, 7, 8) })
        {
            TopologyStatus = goodInterface,
            Groups = new[] { firstGroup }
        };
        DeviceSnapshot second = new("SECRET_DEVICE_B", "192.168.77.22", "SECRET_ROOM_B", "SECRET_MODEL_B",
            attempt.AddHours(-2), attempt, goodInterface, badDescription, [])
        {
            TopologyStatus = badDescription,
            Groups = [secondGroup]
        };
        DiscoveryRangeSnapshot[] ranges =
        [
            new("192.168.77.0/24", attempt, attempt, false, 2, 1, "SECRET_RANGE_ERROR")
        ];
        return new(true, attempt, attempt, new[] { first, second }, ranges, true)
        {
            Ssdp = new(true, attempt, attempt, 2, 1, false, "SECRET_SSDP_ERROR"),
            TopologyDiscovery = new(attempt, 4, true)
        };
    }

    /// <summary>Creates a device record for mutating the source collection after export.</summary>
    /// <param name="deviceId">Distinct marker proving the input array was changed.</param>
    /// <returns>A minimal source device.</returns>
    private static DeviceSnapshot CreateEmptyDevice(string deviceId) => new(deviceId, "192.0.2.1", null, null,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, new("description", true, null, null, false, null),
        new("ifconfig", true, null, null, false, null), []);
}
