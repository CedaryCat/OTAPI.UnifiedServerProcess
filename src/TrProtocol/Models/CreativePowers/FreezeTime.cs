namespace TrProtocol.Models.CreativePowers;

public partial class FreezeTime : CreativePower
{
    public sealed override CreativePowerTypes PowerType => CreativePowerTypes.FreezeTime;
    public ASharedTogglePowerData Data;
}
