﻿using Microsoft.Xna.Framework;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncItemsWithShimmer : INetPacket, IItemSlot
{
    public readonly MessageID Type => MessageID.SyncItemsWithShimmer;
    public short ItemSlot { get; set; }
    public Vector2 Position;
    public Vector2 Velocity;
    public short Stack;
    public byte Prefix;
    public byte Owner;
    public short ItemType;
    public bool Shimmered;
    public float ShimmerTime;
}