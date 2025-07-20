using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
	public class LiquidCtlPlugin(IPluginLogger logger) : IPlugin2
	{
		public string Name => "liquidctl";

		private Dictionary<string, DeviceSensor> sensors = [];
		private readonly IPluginLogger _logger = logger;
		private readonly LiquidctlBridgeWrapper liquidctl = new LiquidctlBridgeWrapper(logger);

		public void Close()
		{
			return;
		}

		public void Initialize()
		{
			liquidctl.InitAll();
		}

		public void Load(IPluginSensorsContainer _container)
		{
			var detected_devices = liquidctl.GetStatuses();
			var supported_units = new List<string> { "°C", "rpm", "%" };
			foreach (var device in detected_devices)
			{
				foreach (var channel in device.status)
				{
					if (!supported_units.Contains(channel.unit) || channel.value == null) { continue; }
					if (channel.unit == "%")
					{
						var sensor = new ControlSensor(device, channel, liquidctl);
						sensors[sensor.Id] = sensor;
						_container.ControlSensors.Add(sensor);
					}
					else
					{
						var sensor = new DeviceSensor(device, channel);
						sensors[sensor.Id] = sensor;
						if (channel.unit == "rpm") { _container.FanSensors.Add(sensor); }
						if (channel.unit == "°C") { _container.TempSensors.Add(sensor); }
					}
				}
			}
		}


		public void Update()
		{
			var detected_devices = liquidctl.GetStatuses();
			foreach (var device in detected_devices)
			{
				foreach (var channel in device.status)
				{
					if (channel.value == null) { continue; }
					var sensor = new DeviceSensor(device, channel);
					if (!sensors.ContainsKey(sensor.Id)) { continue; }
					sensors[sensor.Id].Update(channel);
				}
			}
		}
	}
}