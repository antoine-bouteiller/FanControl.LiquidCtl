using FanControl.Plugins;


namespace FanControl.LiquidCtl
{
	public class StatusValue
	{
		public required string key { get; set; }
		public double? value { get; set; }
		public required string unit { get; set; }
	}

	public class DeviceStatus
	{
		public required int id { get; set; }
		public required string bus { get; set; }
		public required string address { get; set; }
		public required string description { get; set; }
		public required List<StatusValue> status { get; set; }
	}

	public class DeviceSensor : IPluginSensor
	{
		public string Id => $"{Device.description}/{Channel.key}".Replace(" ", "");
		public string Name => $"{Device.description}: {Channel.key}";
		public float? Value => (float?)Channel.value;

		public void Update() { }
		internal void Update(StatusValue status)
		{
			Channel = status;
		}

		internal DeviceStatus Device { get; }
		internal StatusValue Channel { get; set; }
		internal DeviceSensor(DeviceStatus device, StatusValue channel)
		{
			Device = device;
			Channel = channel;
		}
	}

	public class ControlSensor : DeviceSensor, IPluginControlSensor
	{
		internal float? Initial { get; }
		private readonly LiquidctlBridgeWrapper liquidctl;
		internal ControlSensor(DeviceStatus device, StatusValue channel, LiquidctlBridgeWrapper liquidctl) :
			base(device, channel)
		{
			Initial = Value;
			this.liquidctl = liquidctl;
		}

		public void Reset()
		{
			if (Initial != null)
			{
				Set(Initial.GetValueOrDefault());
			}
		}

		public void Set(float val)
		{
			liquidctl.SetFixedSpeed(new FixedSpeedRequest
			{
				device_id = Device.id,
				speed_kwargs = new SpeedKwargs
				{
					duty = (int)Math.Round(val),
					channel = Channel.key.Replace(" ", "")
				}
			});
		}
	}
}