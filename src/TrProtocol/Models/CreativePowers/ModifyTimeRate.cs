namespace TrProtocol.Models.CreativePowers;

public partial class ModifyTimeRate : CreativePower
{
    public sealed override CreativePowerTypes PowerType => CreativePowerTypes.TimeSpeed;
    public ASharedSliderPowerData Data;
}
