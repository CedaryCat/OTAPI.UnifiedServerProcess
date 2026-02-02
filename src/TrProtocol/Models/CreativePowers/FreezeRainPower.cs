namespace TrProtocol.Models.CreativePowers;

public partial class FreezeRainPower : CreativePower
{
    public sealed override CreativePowerTypes PowerType => CreativePowerTypes.RainFreeze;
    public ASharedTogglePowerData Data;
}
