﻿using Microsoft.Xna.Framework;
using Terraria;
using TrProtocol.Attributes;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncProjectile : INetPacket, IProjSlot, IPlayerSlot
{
    public readonly MessageID Type => MessageID.SyncProjectile;
    public short ProjSlot { get; set; }
    public Vector2 Position;
    public Vector2 Velocity;
    public byte PlayerSlot { get; set; }
    //[Bounds("Terraria238", 955)]
    public short ProjType;
    public BitsByte Bit1;
    [Condition(nameof(Bit1), 2)]
    public BitsByte Bit2;
    [Condition(nameof(Bit1), 0)]
    public float AI1;
    [Condition(nameof(Bit1), 1)]
    public float AI2;
    [Condition(nameof(Bit1), 3)]
    public ushort BannerId;
    [Condition(nameof(Bit1), 4)]
    public short Damage;
    [Condition(nameof(Bit1), 5)]
    public float Knockback;
    [Condition(nameof(Bit1), 6)]
    public short OriginalDamage;
    [Condition(nameof(Bit1), 7)]
    public short UUID;
    [Condition(nameof(Bit2), 0)]
    public float AI3;

}