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

            IReadOnlyCollection<DeviceStatus> detected_devices = liquidctl.GetStatuses();

            foreach (DeviceStatus device in detected_devices)
            {
                foreach (StatusValue channel in device.Status)
                {
                    ProcessChannel(device, channel, _container);
                }

                AddAuthoritativeControls(device, _container);
            }
        }

        private void AddAuthoritativeControls(DeviceStatus device, IPluginSensorsContainer container)
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
                string pairedId = Utils.CreateSensorId(device.Description, Utils.GetSpeedKeyFromDutyKey(dutyKey));
                ControlSensor sensor = new(device, dutyChannel, liquidctl, pairedId, explicitChannelName: channelName);
                sensors[sensor.Id] = sensor;
                container.ControlSensors.Add(sensor);
            }
        }

        private void ProcessChannel(DeviceStatus device, StatusValue channel, IPluginSensorsContainer container)
        {
            if (channel.Value == null) { return; }

            switch (channel.Unit)
            {
                case "%":
                    AddControlSensor(device, channel, container);
                    break;
                case "rpm":
                    AddReadSensor(device, channel, container.FanSensors);
                    break;
                case "°C":
                    AddReadSensor(device, channel, container.TempSensors);
                    break;
            }
        }

        private void AddControlSensor(DeviceStatus device, StatusValue channel, IPluginSensorsContainer container)
        {
            string speedChannelKey = Utils.GetSpeedKeyFromDutyKey(channel.Key);
            string speedSensorId = Utils.CreateSensorId(device.Description, speedChannelKey);
            ControlSensor sensor = new(device, channel, liquidctl, speedSensorId);
            sensors[sensor.Id] = sensor;
            container.ControlSensors.Add(sensor);
        }

        private void AddReadSensor(DeviceStatus device, StatusValue channel, List<IPluginSensor> sensorList)
        {
            DeviceSensor sensor = new(device, channel);
            sensors[sensor.Id] = sensor;
            sensorList.Add(sensor);
        }


        public void Update()
        {
            IReadOnlyCollection<DeviceStatus> detected_devices = liquidctl.GetStatuses();
            foreach (DeviceStatus device in detected_devices)
            {
                foreach (StatusValue channel in device.Status)
                {
                    if (channel.Value == null) { continue; }
                    string sensorId = Utils.CreateSensorId(device.Description, channel.Key);
                    if (!sensors.ContainsKey(sensorId)) { continue; }
                    sensors[sensorId].Update(channel);
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
