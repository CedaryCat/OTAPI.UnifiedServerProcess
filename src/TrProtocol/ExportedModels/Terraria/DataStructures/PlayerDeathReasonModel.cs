using TrProtocol.Attributes;

namespace Terraria.DataStructures {
    public class PlayerDeathReasonModel {
        public DeathSourceFlags Flags {
            get {
                DeathSourceFlags flags = default;
                flags.HasPlayerSource = _sourcePlayerIndex != -1;
                flags.HasNPCSource = _sourceNPCIndex != -1;
                flags.HasProjectileLocalSource = _sourceProjectileLocalIndex != -1;
                flags.HasOtherSource = _sourceOtherIndex != -1;
                flags.HasProjectileType = _sourceProjectileType != 0;
                flags.HasItemType = _sourceItemType != 0;
                flags.HasItemPrefix = _sourceItemPrefix != 0;
                flags.HasCustomReason = _sourceCustomReason != null;
                return flags;
            }
            set {
                var current = Flags;
                if (value.HasPlayerSource != current.HasPlayerSource)
                    _sourcePlayerIndex = value.HasPlayerSource ? 0 : -1;
                if (value.HasNPCSource != current.HasNPCSource)
                    _sourceNPCIndex = value.HasNPCSource ? 0 : -1;
                if (value.HasProjectileLocalSource != current.HasProjectileLocalSource)
                    _sourceProjectileLocalIndex = value.HasProjectileLocalSource ? 0 : -1;
                if (value.HasOtherSource != current.HasOtherSource)
                    _sourceOtherIndex = value.HasOtherSource ? 0 : -1;
                if (value.HasProjectileType != current.HasProjectileType)
                    _sourceProjectileType = value.HasProjectileType ? 1 : 0;
                if (value.HasItemType != current.HasItemType)
                    _sourceItemType = value.HasItemType ? 1 : 0;
                if (value.HasItemPrefix != current.HasItemPrefix)
                    _sourceItemPrefix = value.HasItemPrefix ? 1 : 0;
                if (value.HasCustomReason != current.HasCustomReason)
                    _sourceCustomReason = value.HasCustomReason ? "" : null;
            }
        }
        [Condition(nameof(Flags.HasPlayerSource))]
        [SerializeAs(typeof(short))] public int _sourcePlayerIndex = -1;
        [Condition(nameof(Flags.HasNPCSource))]
        [SerializeAs(typeof(short))] public int _sourceNPCIndex = -1;
        [Condition(nameof(Flags.HasProjectileLocalSource))]
        [SerializeAs(typeof(short))] public int _sourceProjectileLocalIndex = -1;
        [Condition(nameof(Flags.HasOtherSource))]
        [SerializeAs(typeof(byte))] public int _sourceOtherIndex = -1;
        [Condition(nameof(Flags.HasProjectileType))]
        [SerializeAs(typeof(short))] public int _sourceProjectileType;
        [Condition(nameof(Flags.HasItemType))]
        [SerializeAs(typeof(short))] public int _sourceItemType;
        [Condition(nameof(Flags.HasItemPrefix))]
        [SerializeAs(typeof(byte))] public int _sourceItemPrefix;
        [Condition(nameof(Flags.HasCustomReason))]
        public string? _sourceCustomReason;
    }
    public struct DeathSourceFlags {
        public byte Value;
        public bool HasPlayerSource {
            readonly get => (Value & 1) != 0;
            set => Value = (byte)(Value & ~1 | (value ? 1 : 0));
        }
        public bool HasNPCSource {
            readonly get => (Value & 2) != 0;
            set => Value = (byte)(Value & ~2 | (value ? 2 : 0));
        }
        public bool HasProjectileLocalSource {
            readonly get => (Value & 4) != 0;
            set => Value = (byte)(Value & ~4 | (value ? 4 : 0));
        }
        public bool HasOtherSource {
            readonly get => (Value & 8) != 0;
            set => Value = (byte)(Value & ~8 | (value ? 8 : 0));
        }
        public bool HasProjectileType {
            readonly get => (Value & 16) != 0;
            set => Value = (byte)(Value & ~16 | (value ? 16 : 0));
        }
        public bool HasItemType {
            readonly get => (Value & 32) != 0;
            set => Value = (byte)(Value & ~32 | (value ? 32 : 0));
        }
        public bool HasItemPrefix {
            readonly get => (Value & 64) != 0;
            set => Value = (byte)(Value & ~64 | (value ? 64 : 0));
        }
        public bool HasCustomReason {
            readonly get => (Value & 128) != 0;
            set => Value = (byte)(Value & ~128 | (value ? 128 : 0));
        }
        public static implicit operator byte(DeathSourceFlags f) => f.Value;
        public static explicit operator DeathSourceFlags(byte b) => new DeathSourceFlags { Value = b };
    }
}
