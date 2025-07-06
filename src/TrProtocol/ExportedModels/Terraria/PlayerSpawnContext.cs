using TrProtocol.Attributes;

namespace Terraria;
public enum PlayerSpawnContext : byte {
    ReviveFromDeath,
    SpawningIntoWorld,
    RecallFromItem
}
