using TrProtocol.Models;
using Terraria.GameContent.Drawing;

namespace TrProtocol.NetPackets.Modules;

public partial struct NetParticlesModule : INetModulesPacket
{
    public readonly NetModuleType ModuleType => NetModuleType.NetParticlesModule;
    public ParticleOrchestraType ParticleType;
    public ParticleOrchestraSettings Setting;
}
