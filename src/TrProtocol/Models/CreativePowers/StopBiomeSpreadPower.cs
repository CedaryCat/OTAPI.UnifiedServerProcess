namespace TrProtocol.Models.CreativePowers;

public partial class StopBiomeSpreadPower : CreativePower
{
    public sealed override CreativePowerTypes PowerType => CreativePowerTypes.BiomeSpreadFreeze;
    public ASharedTogglePowerData Data;
}
