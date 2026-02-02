namespace TrProtocol.Models.CreativePowers;

public partial class DifficultySliderPower : CreativePower
{
    public sealed override CreativePowerTypes PowerType => CreativePowerTypes.WorldDifficulty;
    public ASharedSliderPowerData Data;
}
