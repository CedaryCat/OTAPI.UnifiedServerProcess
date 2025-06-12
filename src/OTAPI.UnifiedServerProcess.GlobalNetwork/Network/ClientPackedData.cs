using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork.Network
{
    public record ClientPackedData(
        Player Player, 
        RemoteClient RemoteClient,
        MessageBuffer Buffer) { }
}
