﻿#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS0436 // Type conflicts with imported type
using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Terraria;

[Modification(ModType.PostMerge, "Netplay Send Data Check", ModPriority.Early)]
[MonoMod.MonoModIgnore]
void NetplayConnectionCheck(ModFwModder modder) {

    List<MethodDefinition> methods = [];
    foreach (var type in modder.Module.GetAllTypes()) { 
        foreach (var method in type.Methods) {
            if (!method.HasBody || method.Name == nameof(NetMessage.CheckCanSend)) {
                continue;
            }
            if (method.Body.Instructions.Any(inst => inst is { Operand: MemberReference { Name: "IsConnected", DeclaringType.Name: "RemoteClient" } })) {
                methods.Add(method);
                continue;
            }
        }
    }


    var netplay = modder.Module.GetType("Terraria.Netplay");
    var checkConnectionMDef = modder.Module.ImportReference(typeof(NetMessage).GetMethod(nameof(NetMessage.CheckCanSend))!);
    foreach (var method in methods) {
        var cursor = method.GetILCursor();

        while (cursor.TryGotoNext(MoveType.Before,
            inst => inst.MatchLdsfld("Terraria.Netplay", "Clients"),
            inst => true,
            inst => inst.MatchLdelemRef(),
            inst => inst.MatchCallvirt("Terraria.RemoteClient", "IsConnected"))) {

            var loadClients = cursor.Next!;
            var loadIndex = cursor.Next!.Next!;
            var loadElement = cursor.Next!.Next!.Next;
            var checkConnection = cursor.Next!.Next!.Next!.Next;

            loadClients.OpCode = OpCodes.Nop;
            loadClients.Operand = null;

            loadElement.OpCode = OpCodes.Nop;
            loadElement.Operand = null;

            checkConnection.OpCode = OpCodes.Call;
            checkConnection.Operand = checkConnectionMDef;
        }
    }
}

namespace Terraria
{
    public class NetMessage
    {
        public static bool CheckCanSend(int clientIndex) {
            return Netplay.Clients[clientIndex].IsConnected();
        }
    }
    namespace Net
    {
        public class patch_NetManager : NetManager
        {
            [MonoMod.MonoModReplace]
            public new void mfwh_Broadcast(NetPacket packet, [Optional] int ignoreClient) {
                for (int i = 0; i < 256; i++) {
                    if (i != ignoreClient && NetMessage.CheckCanSend(i)) {
                        SendData(Netplay.Clients[i].Socket, packet);
                    }
                }
            }

            [MonoMod.MonoModReplace]
            public new void mfwh_Broadcast(NetPacket packet, BroadcastCondition conditionToBroadcast, [Optional] int ignoreClient) {
                for (int i = 0; i < 256; i++) {
                    if (i != ignoreClient && NetMessage.CheckCanSend(i) && conditionToBroadcast(i)) {
                        SendData(Netplay.Clients[i].Socket, packet);
                    }
                }
            }

            [MonoMod.MonoModReplace]
            public new void mfwh_SendToClient(NetPacket packet, int playerId) {
                if (!NetMessage.CheckCanSend(playerId)) {
                    return;
                }
                SendData(Netplay.Clients[playerId].Socket, packet);
            }
        }
    }
}
