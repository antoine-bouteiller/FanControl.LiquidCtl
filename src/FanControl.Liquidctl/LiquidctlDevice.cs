using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    public class DeviceSensor : IPluginSensor
    {
        public string Id => Utils.CreateSensorId(Description, Channel.Key);
        public virtual string Name => Utils.CreateSensorName(Description, Channel.Key);
        public float? Value => (float?)Channel.Value;

        public void Update() { }

        internal void Update(StatusValue status)
        {
            Channel = status;
        }

        internal DeviceStatus Device { get; }
        internal string Description { get; }
        internal StatusValue Channel { get; set; }

        internal DeviceSensor(DeviceStatus device, string description, StatusValue channel)
        {
            Device = device;
            Description = description;
            Channel = channel;
        }
    }

    public class ControlSensor : DeviceSensor, IPluginControlSensor2
    {
        internal float? Initial { get; }
        private readonly ILiquidctlClient liquidctl;
        private readonly string channelName;

        public string? PairedFanSensorId { get; }

        internal ControlSensor(DeviceStatus device, string description, StatusValue channel, ILiquidctlClient liquidctl, string? pairedFanSensorId, string? explicitChannelName = null) :
            base(device, description, channel)
        {
            Initial = Value;
            this.liquidctl = liquidctl;
            channelName = explicitChannelName ?? Utils.ExtractChannelName(channel.Key);
            PairedFanSensorId = pairedFanSensorId;
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
                    Channel = channelName
                }
            });
        }
    }
}
