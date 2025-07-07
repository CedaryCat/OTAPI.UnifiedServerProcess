using Microsoft.Xna.Framework;
using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetPingModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetPingModule;
    public Vector2 Position;
}
