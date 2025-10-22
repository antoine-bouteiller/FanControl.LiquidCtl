using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
	public class LiquidCtlPlugin(IPluginLogger logger) : IPlugin2, IDisposable
	{
		public string Name => "liquidctl";

		private readonly Dictionary<string, DeviceSensor> sensors = [];
		private readonly LiquidctlBridgeWrapper liquidctl = new(logger);
		private bool _disposed;

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
