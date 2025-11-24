using FanControl.Plugins;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

[assembly: InternalsVisibleTo("FanControl.Liquidctl.Tests")]

namespace FanControl.LiquidCtl
{
	public class StatusValue
	{
		[JsonPropertyName("key")]
		public required string Key { get; set; }

		[JsonPropertyName("value")]
		public double? Value { get; set; }

		[JsonPropertyName("unit")]
		public required string Unit { get; set; }
	}

	public class DeviceStatus
	{
		[JsonPropertyName("id")]
		public required int Id { get; set; }

		[JsonPropertyName("bus")]
		public required string Bus { get; set; }

		[JsonPropertyName("address")]
		public required string Address { get; set; }

		[JsonPropertyName("description")]
		public required string Description { get; set; }

		[JsonPropertyName("status")]
		public required IReadOnlyCollection<StatusValue> Status { get; init; }
	}

	public class DeviceSensor : IPluginSensor
	{
		public string Id => $"{Device.Description}/{Channel.Key}".Replace(" ", "", StringComparison.Ordinal);
		public string Name => $"{Device.Description}: {Channel.Key}";
		public float? Value => (float?)Channel.Value;

		public void Update() { }
		public void Update(StatusValue status)
		{
			Channel = status;
		}

		public DeviceStatus Device { get; }
		public StatusValue Channel { get; set; }
		public DeviceSensor(DeviceStatus device, StatusValue channel)
		{
			Device = device;
			Channel = channel;
		}
	}

	public class ControlSensor : DeviceSensor, IPluginControlSensor2
	{
		public float? Initial { get; }
		private readonly LiquidctlBridgeWrapper liquidctl;

		public string? PairedFanSensorId { get; set; }

		public ControlSensor(DeviceStatus device, StatusValue channel, LiquidctlBridgeWrapper liquidctl) :
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
