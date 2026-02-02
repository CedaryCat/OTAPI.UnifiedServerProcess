namespace TrProtocol.Models.CreativePowers;

public partial class FreezeWindDirectionAndStrength : CreativePower
{
    public sealed override CreativePowerTypes PowerType => CreativePowerTypes.WindFreeze;
    public ASharedTogglePowerData Data;
}
