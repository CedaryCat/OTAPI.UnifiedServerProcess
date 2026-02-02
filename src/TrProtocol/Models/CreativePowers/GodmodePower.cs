namespace TrProtocol.Models.CreativePowers;

public partial class GodmodePower : CreativePower
{
    public sealed override CreativePowerTypes PowerType => CreativePowerTypes.Godmode;
    public APerPlayerTogglePowerData Data;
}