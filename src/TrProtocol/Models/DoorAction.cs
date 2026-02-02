namespace TrProtocol.Models;

public enum DoorAction : byte
{
    OpenDoor = 0,
    CloseDoor,
    OpenTrapdoor,
    CloseTrapdoor,
    OpenTallGate,
    CloseTallGate
}
