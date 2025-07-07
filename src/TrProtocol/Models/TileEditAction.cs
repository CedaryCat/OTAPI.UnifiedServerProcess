namespace TrProtocol.Models
{
    public enum TileEditAction : byte
    {
        KillTile = 0,
        PlaceTile,
        KillWall,
        PlaceWall,
        KillTileNoItem,
        PlaceWire,
        KillWire,
        PoundTile,
        PlaceActuator,
        KillActuator,
        PlaceWire2,
        KillWire2,
        PlaceWire3,
        KillWire3,
        SlopeTile,
        FrameTrack,
        PlaceWire4,
        KillWire4,
        PokeLogicGate,
        Acutate,
        TryKillTile,
        ReplaceTile,
        ReplaceWall,
        SlopePoundTile
    }
}
