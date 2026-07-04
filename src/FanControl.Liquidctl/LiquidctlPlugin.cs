using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    public sealed class LiquidCtlPlugin : IPlugin2, IDisposable
    {
        public string Name => "liquidctl";

        private readonly Dictionary<string, DeviceSensor> sensors = [];
        private readonly ILiquidctlClient liquidctl;
        private bool _disposed;

        public LiquidCtlPlugin(IPluginLogger logger) : this(new LiquidctlClient(logger)) { }

        internal LiquidCtlPlugin(ILiquidctlClient client)
        {
            liquidctl = client;
        }

        public void Close()
        {
            liquidctl.Dispose();
        }

        public void Initialize()
        {
            liquidctl.Init();
        }

        public void Load(IPluginSensorsContainer _container)
        {
            ArgumentNullException.ThrowIfNull(_container);

            SensorSet mapped = SensorMapper.Map(liquidctl.GetStatuses(), liquidctl);

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
            ISet<string> duplicates = SensorMapper.DuplicateDescriptions(devices);

            foreach (DeviceStatus device in devices)
            {
                string description = SensorMapper.EffectiveDescription(device, duplicates);
                foreach (StatusValue channel in device.Status)
                {
                    if (channel.Value == null) { continue; }
                    if (sensors.TryGetValue(Utils.CreateSensorId(description, channel.Key), out DeviceSensor? sensor))
                    {
                        sensor.Update(channel);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            liquidctl.Dispose();
        }
    }
}
