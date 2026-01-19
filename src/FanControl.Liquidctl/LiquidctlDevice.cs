using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    public class DeviceSensor : IPluginSensor
    {
        public string Id => Utils.CreateSensorId(Device.Description, Channel.Key);
        public virtual string Name => Utils.CreateSensorName(Device.Description, Channel.Key);
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
        private readonly LiquidctlClient liquidctl;
        private readonly string channelName;

        public string? PairedFanSensorId { get; }

        internal ControlSensor(DeviceStatus device, StatusValue channel, LiquidctlClient liquidctl, string? pairedFanSensorId) :
            base(device, channel)
        {
            Initial = Value;
            this.liquidctl = liquidctl;
            channelName = Utils.ExtractChannelName(channel.Key);
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
