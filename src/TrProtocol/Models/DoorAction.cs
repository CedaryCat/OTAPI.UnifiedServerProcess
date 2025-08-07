using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrProtocol.Models
{
    public enum DoorAction : byte
    {
        OpenDoor = 0,
        CloseDoor,
        OpenTrapdoor,
        CloseTrapdoor,
        OpenTallGate,
        CloseTallGate
    }
}
