namespace FanControl.LiquidCtl
{
    internal sealed class SensorSet
    {
        public List<DeviceSensor> Fans { get; } = [];
        public List<DeviceSensor> Temps { get; } = [];
        public List<ControlSensor> Controls { get; } = [];
    }

    internal static class SensorMapper
    {
        internal static SensorSet Map(IReadOnlyList<DeviceStatus> devices, ILiquidctlClient client)
        {
            var mapped = new SensorSet();
            Dictionary<int, string> descriptionsById = EffectiveDescriptionsById(devices);

            foreach (DeviceStatus device in devices)
            {
                string description = descriptionsById[device.Id];

                foreach (StatusValue channel in device.Status)
                {
                    if (channel.Value == null) { continue; }

                    switch (channel.Unit)
                    {
                        case "%":
                            mapped.Controls.Add(CreateControl(device, description, channel, client));
                            break;
                        case "rpm":
                            mapped.Fans.Add(new DeviceSensor(device, description, channel));
                            break;
                        case "°C":
                            mapped.Temps.Add(new DeviceSensor(device, description, channel));
                            break;
                    }
                }

                AddAuthoritativeControls(device, description, client, mapped);
            }

            return mapped;
        }

        internal static ISet<string> DuplicateDescriptions(IReadOnlyList<DeviceStatus> devices)
        {
            return devices
                .GroupBy(d => d.Description, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
        }

        internal static string EffectiveDescription(DeviceStatus device, ISet<string> duplicates)
        {
            return duplicates.Contains(device.Description)
                ? $"{device.Description} #{device.Id}"
                : device.Description;
        }

        // Single source of truth for device id -> effective description, so
        // Load (via Map) and Update always agree even if devices drop out.
        internal static Dictionary<int, string> EffectiveDescriptionsById(IReadOnlyList<DeviceStatus> devices)
        {
            ISet<string> duplicates = DuplicateDescriptions(devices);
            return devices.ToDictionary(device => device.Id, device => EffectiveDescription(device, duplicates));
        }

        private static ControlSensor CreateControl(DeviceStatus device, string description, StatusValue channel, ILiquidctlClient client)
        {
            string speedChannelKey = Utils.GetSpeedKeyFromDutyKey(channel.Key);
            string speedSensorId = Utils.CreateSensorId(description, speedChannelKey);
            return new ControlSensor(device, description, channel, client, speedSensorId);
        }

        private static void AddAuthoritativeControls(DeviceStatus device, string description, ILiquidctlClient client, SensorSet mapped)
        {
            foreach (string channelName in device.SpeedChannels)
            {
                if (device.Status.Any(s => s.Unit == "%" && Utils.ExtractChannelName(s.Key) == channelName))
                {
                    continue;
                }

                StatusValue? speed = device.Status.FirstOrDefault(
                    s => s.Unit == "rpm" && Utils.ExtractChannelName(s.Key) == channelName);
                string dutyKey = speed != null ? Utils.GetDutyKeyFromSpeedKey(speed.Key) : channelName;

                StatusValue dutyChannel = new() { Key = dutyKey, Value = null, Unit = "%" };
                // No real rpm sensor to pair with when speed is unreported, so
                // leave the id unset rather than fabricating one that never matches.
                string? pairedId = speed != null ? Utils.CreateSensorId(description, Utils.GetSpeedKeyFromDutyKey(dutyKey)) : null;
                mapped.Controls.Add(new ControlSensor(device, description, dutyChannel, client, pairedId, explicitChannelName: channelName));
            }
        }
    }
}
