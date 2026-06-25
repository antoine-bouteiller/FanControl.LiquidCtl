namespace FanControl.LiquidCtl
{
    internal interface ILiquidctlClient : IDisposable
    {
        void Init();
        IReadOnlyList<DeviceStatus> GetStatuses();
        void SetFixedSpeed(FixedSpeedRequest request);
    }
}
