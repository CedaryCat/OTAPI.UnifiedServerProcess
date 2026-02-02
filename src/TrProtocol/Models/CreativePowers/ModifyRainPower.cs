namespace TrProtocol.Models.CreativePowers;

public partial class ModifyRainPower : CreativePower
{
    public sealed override CreativePowerTypes PowerType => CreativePowerTypes.RainStrength;
    public ASharedSliderPowerData Data;
}
