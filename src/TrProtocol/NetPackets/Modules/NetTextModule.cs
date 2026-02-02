using Microsoft.Xna.Framework;
using Terraria.Localization;
using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetTextModule : INetModulesPacket, ISideSpecific
{
    public readonly NetModuleType ModuleType => NetModuleType.NetTextModule;
    [C2SOnly]
    public TextC2S? TextC2S;
    [S2COnly]
    public TextS2C? TextS2C;
    public override string ToString() {
        if (TextC2S is not null) {
            return $"[S2C] {TextS2C}";
        }
        else if (TextC2S is not null) {
            return $"[C2S] {TextC2S}";
        }
        else {
            return "";
        }
    }
}
public class TextC2S
{
    public string? Command;
    public string? Text;
}
public class TextS2C
{
    public byte PlayerSlot;
    public NetworkText? Text;
    public Color Color;
}
