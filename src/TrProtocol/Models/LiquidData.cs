using TrProtocol.Attributes;

namespace TrProtocol.Models;

public partial struct LiquidData {
    public ushort TotalChanges;
    [ArraySize(nameof(TotalChanges))]
    public LiquidChange[] LiquidChanges;
}
