namespace TrProtocol.Models.CreativePowers;

public partial class SpawnRateSliderPerPlayerPower : CreativePower
{
    public sealed override CreativePowerTypes PowerType => CreativePowerTypes.SetSpawnRate;
    public APerPlayerSliderPowerData Data;
}
