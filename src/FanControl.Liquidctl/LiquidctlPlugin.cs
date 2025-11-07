using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
	public class LiquidCtlPlugin(IPluginLogger logger) : IPlugin3, IDisposable
	{
		public string Name => "liquidctl";

		private readonly Dictionary<string, DeviceSensor> sensors = [];
		private readonly LiquidctlBridgeWrapper liquidctl = new(logger);
		private bool _disposed;

		private Action? _refreshRequested;
		public event Action RefreshRequested
		{
			add => _refreshRequested += value;
			remove => _refreshRequested -= value;
		}

		public void Close()
		{
			liquidctl.Shutdown();
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

			// Create all sensors
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

			// Auto-link control sensors to their corresponding speed sensors
			// The Python bridge already strips "duty" from control channel keys via _formatString()
			// So control channels come as "pump", "fan1", etc. (not "pump duty")
			// Speed sensors come as "pump speed", "fan1 speed", etc.
			// After space removal in the ID, we get "pumpspeed", "fan1speed", etc.
			foreach (DeviceSensor sensor in sensors.Values)
			{
				if (sensor is not ControlSensor controlSensor) { continue; }

				// Build the expected speed sensor key by appending " speed" to the control key
				// E.g., "pump" → "pump speed" → "pumpspeed" (after space removal)
				string speedChannelKey = $"{controlSensor.Channel.Key} speed";
				string potentialSpeedSensorId = $"{controlSensor.Device.Description}/{speedChannelKey}".Replace(" ", "", StringComparison.Ordinal);

				// Check if this speed sensor exists
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
					DeviceSensor sensor = new(device, channel);
					if (!sensors.ContainsKey(sensor.Id)) { continue; }
					sensors[sensor.Id].Update(channel);
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
