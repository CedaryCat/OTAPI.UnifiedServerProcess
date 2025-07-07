using Terraria.GameContent.Drawing;
using TrProtocol.Models;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetParticlesModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetParticlesModule;
    public ParticleOrchestraType ParticleType;
    public ParticleOrchestraSettings Setting;
}
