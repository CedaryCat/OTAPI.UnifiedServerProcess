namespace Terraria.GameContent.NetModules;

public class NetTeleportPylonModule
{
    public enum SubPacketType : byte
    {
        PylonWasAdded,
        PylonWasRemoved,
        PlayerRequestsTeleport
    }
}
