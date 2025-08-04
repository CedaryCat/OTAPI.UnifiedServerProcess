using Terraria;
using TrProtocol.Attributes;

namespace TrProtocol.Models;
public struct SimpleTileData
{
    public BitsByte Flags1;
    public BitsByte Flags2;
    public BitsByte Flags3;
    [Condition(nameof(Flags2), 2)]
    public byte TileColor;
    [Condition(nameof(Flags2), 3)]
    public byte WallColor;
    [Condition(nameof(Flags1), 0)]
    public ushort TileType;
    public readonly bool FrameXYExist => Constants.tileFrameImportant[TileType];
    [Condition(nameof(Flags1), 0), Condition(nameof(FrameXYExist), true)]
    public short FrameX;
    [Condition(nameof(Flags1), 0), Condition(nameof(FrameXYExist), true)]
    public short FrameY;
    [Condition(nameof(Flags1), 2)]
    public ushort WallType;
    [Condition(nameof(Flags1), 3)]
    public byte Liquid;
    [Condition(nameof(Flags1), 3)]
    public byte LiquidType;


    [IgnoreSerialize] public bool Wire {
        get => Flags1[4];
        set => Flags1[4] = value;
    }
    [IgnoreSerialize] public bool HalfBrick {
        get => Flags1[5];
        set => Flags1[5] = value;
    }
    [IgnoreSerialize] public bool Actuator {
        get => Flags1[6];
        set => Flags1[6] = value;
    }
    [IgnoreSerialize] public bool InActive {
        get => Flags1[7];
        set => Flags1[7] = value;
    }
    [IgnoreSerialize] public bool Wire2 {
        get => Flags2[0];
        set => Flags2[0] = value;
    }
    [IgnoreSerialize] public bool Wire3 {
        get => Flags2[1];
        set => Flags2[1] = value;
    }

    [IgnoreSerialize] public byte Slope {
        get {
            byte slope = 0;
            if (Flags2[4]) {
                slope++;
            }
            if (Flags2[5]) {
                slope += 2;
            }
            if (Flags2[6]) {
                slope += 4;
            }
            return slope;
        }
        set {
            Flags2[4] = (value & 1) == 1;
            Flags2[5] = (value & 2) == 2;
            Flags2[6] = (value & 4) == 4;
        }
    }
    [IgnoreSerialize]
    public bool Wire4 {
        get => Flags2[7];
        set => Flags2[7] = value;
    }
    [IgnoreSerialize]
    public bool FullbrightBlock {
        get => Flags3[0];
        set => Flags3[0] = value;
    }
    [IgnoreSerialize]
    public bool FullbrightWall {
        get => Flags3[1];
        set => Flags3[1] = value;
    }
    [IgnoreSerialize]
    public bool InvisibleBlock {
        get => Flags3[2];
        set => Flags3[2] = value;
    }
    [IgnoreSerialize]
    public bool InvisibleWall {
        get => Flags3[3];
        set => Flags3[3] = value;
    }
}
