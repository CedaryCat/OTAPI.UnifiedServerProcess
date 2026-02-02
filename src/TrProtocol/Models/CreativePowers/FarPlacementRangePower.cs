namespace TrProtocol.Models.CreativePowers;

public partial class FarPlacementRangePower : CreativePower
{
    public sealed override CreativePowerTypes PowerType => CreativePowerTypes.IncreasePlacementRange;
    public APerPlayerTogglePowerData Data;
}
