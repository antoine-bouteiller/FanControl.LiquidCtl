using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    public sealed class LiquidCtlPlugin : IPlugin3, IDisposable
    {
        public string Name => "liquidctl";

        public event Action? RefreshRequested;

        private readonly Dictionary<string, DeviceSensor> sensors = [];
        private readonly Dictionary<int, string> descriptionsById = [];
        private readonly ILiquidctlClient liquidctl;
        private bool _refreshRequested;
        private bool _disposed;

        public LiquidCtlPlugin(IPluginLogger logger) : this(new LiquidctlClient(logger)) { }

        internal LiquidCtlPlugin(ILiquidctlClient client)
        {
            liquidctl = client;
        }

        public void Close()
        {
            Dispose();
        }

        public void Initialize()
        {
            liquidctl.Init();
        }

        public void Load(IPluginSensorsContainer _container)
        {
            ArgumentNullException.ThrowIfNull(_container);

            // FanControl re-calls Load after a RefreshRequested, so mapping
            // state must be rebuilt from scratch.
            sensors.Clear();
            descriptionsById.Clear();
            _refreshRequested = false;

            IReadOnlyList<DeviceStatus> devices = liquidctl.GetStatuses();
            SensorSet mapped = SensorMapper.Map(devices, liquidctl);

            foreach (KeyValuePair<int, string> entry in SensorMapper.EffectiveDescriptionsById(devices))
            {
                descriptionsById[entry.Key] = entry.Value;
            }

            foreach (DeviceSensor sensor in mapped.Fans)
            {
                sensors[sensor.Id] = sensor;
                _container.FanSensors.Add(sensor);
            }
            foreach (DeviceSensor sensor in mapped.Temps)
            {
                sensors[sensor.Id] = sensor;
                _container.TempSensors.Add(sensor);
            }
            foreach (ControlSensor sensor in mapped.Controls)
            {
                sensors[sensor.Id] = sensor;
                _container.ControlSensors.Add(sensor);
            }
        }

        public void Update()
        {
            IReadOnlyList<DeviceStatus> devices = liquidctl.GetStatuses();
            var updatedIds = new HashSet<string>();

            foreach (DeviceStatus device in devices)
            {
                if (!descriptionsById.TryGetValue(device.Id, out string? description))
                {
                    RequestRefreshOnce();
                    continue;
                }

                foreach (StatusValue channel in device.Status)
                {
                    if (channel.Value == null) { continue; }
                    string id = Utils.CreateSensorId(description, channel.Key);
                    if (sensors.TryGetValue(id, out DeviceSensor? sensor))
                    {
                        sensor.Update(channel);
                        updatedIds.Add(id);
                    }
                }
            }

            // Authoritative control sensors are created with Value=null and never
            // appear in status, so MarkStale is a no-op for them by design.
            foreach (DeviceSensor sensor in sensors.Values)
            {
                if (!updatedIds.Contains(sensor.Id))
                {
                    sensor.MarkStale();
                }
            }
        }

        // Latched until the next Load so a lingering unknown device cannot
        // trigger a refresh storm.
        private void RequestRefreshOnce()
        {
            if (_refreshRequested) return;
            _refreshRequested = true;
            RefreshRequested?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            liquidctl.Dispose();
        }
    }
}
