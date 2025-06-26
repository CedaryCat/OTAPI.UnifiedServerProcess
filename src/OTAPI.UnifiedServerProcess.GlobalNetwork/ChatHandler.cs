using OTAPI.UnifiedServerProcess.GlobalNetwork.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.Localization;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork
{
    public class ChatHandler
    {
        readonly Router router;
        public ChatHandler(Router router) {
            this.router = router;
            On.Terraria.Chat.Commands.SayChatCommand.ProcessIncomingMessage += ProcessIncomingMessage;
        }

        private void ProcessIncomingMessage(On.Terraria.Chat.Commands.SayChatCommand.orig_ProcessIncomingMessage orig, Terraria.Chat.Commands.SayChatCommand self, RootContext root, string text, byte clientId) {
            orig(self, root, text, clientId);

            var player = root.Main.player[clientId];

            foreach (var otherServer in router.servers) {
                if (!otherServer.IsRunning || root == otherServer) {
                    continue;
                }
                otherServer.ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral($"[Realm·{root.Name}] <{player.name}>: {text}"), player.ChatColor());
            }
        }
    }
}
