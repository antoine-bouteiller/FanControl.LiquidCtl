using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    public class LiquidCtlPlugin(IPluginLogger logger) : IPlugin2, IDisposable
    {
        public string Name => "liquidctl";

        private readonly Dictionary<string, DeviceSensor> sensors = [];
        private readonly LiquidctlClient liquidctl = new(logger);
        private bool _disposed;

        public void Close()
        {
            liquidctl.Dispose();
            return;
        }

        public void Initialize()
        {
            liquidctl.Init();
        }

        public void Load(IPluginSensorsContainer _container)
        {
            ArgumentNullException.ThrowIfNull(_container);

            IReadOnlyCollection<DeviceStatus> detected_devices = liquidctl.GetStatuses();
            List<string> supported_units = ["°C", "rpm", "%"];

            foreach (DeviceStatus device in detected_devices)
            {
                foreach (StatusValue channel in device.Status)
                {
                    if (!supported_units.Contains(channel.Unit) || channel.Value == null) { continue; }
                    if (channel.Unit == "%")
                    {
                        ControlSensor sensor = new(device, channel, liquidctl);
                        sensors[sensor.Id] = sensor;
                        _container.ControlSensors.Add(sensor);
                    }
                    else
                    {
                        DeviceSensor sensor = new(device, channel);
                        sensors[sensor.Id] = sensor;
                        if (channel.Unit == "rpm") { _container.FanSensors.Add(sensor); }
                        if (channel.Unit == "°C") { _container.TempSensors.Add(sensor); }
                    }
                }
            }

            foreach (DeviceSensor sensor in sensors.Values)
            {
                if (sensor is not ControlSensor controlSensor) { continue; }

                string speedChannelKey = Utils.GetSpeedKeyFromDutyKey(controlSensor.Channel.Key);
                string potentialSpeedSensorId = Utils.CreateSensorId(controlSensor.Device.Description, speedChannelKey);

                if (sensors.TryGetValue(potentialSpeedSensorId, out DeviceSensor? speedSensor) && speedSensor is not ControlSensor)
                {
                    controlSensor.PairedFanSensorId = speedSensor.Id;
                }
            }
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

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    liquidctl.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
