using FanControl.Plugins;
using MessagePack;
using System.Text.Json.Serialization;

namespace FanControl.LiquidCtl
{
    [MessagePackObject]
	public class StatusValue
	{
		[Key("key")]
		public required string Key { get; set; }

		[Key("value")]
		public double? Value { get; set; }

		[Key("unit")]
		public required string Unit { get; set; }
	}

	[MessagePackObject]
	public class DeviceStatus
	{
		[Key("id")]
		public required int Id { get; set; }

		[Key("bus")]
		public required string Bus { get; set; }

		[Key("address")]
		public required string Address { get; set; }

		[Key("description")]
		public required string Description { get; set; }

		[Key("status")]
		public required IReadOnlyCollection<StatusValue> Status { get; init; }
	}

	public class DeviceSensor : IPluginSensor
	{
		public string Id => $"{Device.Description}/{Channel.Key}".Replace(" ", "", StringComparison.Ordinal);
		public string Name => $"{Device.Description}: {Channel.Key}";
		public float? Value => (float?)Channel.Value;

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

	public class ControlSensor : DeviceSensor, IPluginControlSensor2
	{
		internal float? Initial { get; }
		private readonly LiquidctlBridgeWrapper liquidctl;

		public string? PairedFanSensorId { get; internal set; }

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
				DeviceId = Device.Id,
				SpeedKwargs = new SpeedKwargs
				{
					Duty = (int)Math.Round(val),
					Channel = Channel.Key.Replace(" ", "", StringComparison.Ordinal)
				}
			});
		}
	}
}
