using Microsoft.Xna.Framework;
using OTAPI.UnifiedServerProcess.GlobalNetwork.IO;
using OTAPI.UnifiedServerProcess.GlobalNetwork.Network;
using OTAPI.UnifiedServerProcess.GlobalNetwork.Servers;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Terraria.Chat.Commands;
using Terraria.Localization;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork
{
    public class CommandHandler
    {
        readonly Router router;
        public CommandHandler(Router router) {
            this.router = router;
            On.Terraria.Chat.Commands.SayChatCommand.ProcessIncomingMessage += ProcessIncomingMessage;
            On.OTAPI.HooksSystemContext.MainSystemContext.InvokeCommandProcess += ProcessConsoleMessage;
        }

        public void KeepReadingInput() {
            while (true) {
                ProcessUSPCommandText(null, Console.ReadLine()?.Trim() ?? "", byte.MaxValue);
            }
        }

        void ProcessIncomingMessage(On.Terraria.Chat.Commands.SayChatCommand.orig_ProcessIncomingMessage orig, SayChatCommand self,
            RootContext root, string text, byte clientId) {

            if (ProcessUSPCommandText(root, text, clientId)) {
                return;
            }

            orig(self, root, text, clientId);
        }
        bool ProcessConsoleMessage(On.OTAPI.HooksSystemContext.MainSystemContext.orig_InvokeCommandProcess orig, HooksSystemContext.MainSystemContext self,
            string lowered, string raw) {

            if (ProcessUSPCommandText(self.root, raw, byte.MaxValue)) {
                return false;
            }

            return orig(self, lowered, raw);
        }
        bool ProcessUSPCommandText(RootContext? root, string text, byte clientId) {
            ServerContext? triggerServer;
            if (root is ServerContext currentServer) {
                triggerServer = currentServer;
            }
            else if (root is null) {
                triggerServer = null;
            }
            else {
                return false;
            }

            text = text.Trim();

            if (!text.StartsWith('/') && clientId != byte.MaxValue) {
                return false;
            }

            text = text.ToLower();

            var textArgs = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = textArgs[0].TrimStart('/');
            var args = textArgs.Skip(1).ToArray();

            ProcessUSPCommand(new(triggerServer, clientId), command, args);
            return true;
        }

        ServerContext? FindServer(string name) {
            var servers = router.servers;
            for (int i = 0; i < servers.Length; i++) {
                ServerContext server = servers[i];
                if (!server.IsRunning) {
                    continue;
                }
                if (server.Name == name) {
                    return server;
                }
            }
            if (int.TryParse(name, out int id)) {
                var index = id - 1;
                if (index >= 0 && index < servers.Length && servers[index].IsRunning) {
                    return servers[index];
                }
            }
            return null;
        }
        record Excutor(ServerContext? TriggerServer, byte UserId)
        {
            public ServerContext? TriggerServer = TriggerServer;
            public bool IsServer => TriggerServer is null || UserId == byte.MaxValue;
            [MemberNotNullWhen(true, nameof(TriggerServer))]
            public bool IsClient => TriggerServer is not null && UserId != byte.MaxValue;
            public void Chat(string message, Color color) {
                if (TriggerServer is null) {
                    Console.ForegroundColor = color.ToConsoleColor();
                    Console.WriteLine(message);
                    Console.ResetColor();
                }
                else if (UserId == byte.MaxValue) {
                    TriggerServer.Console.ForegroundColor = color.ToConsoleColor();
                    TriggerServer.Console.WriteLine(message);
                    TriggerServer.Console.ForegroundColor = ConsoleColor.Gray;
                }
                else {
                    TriggerServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral(message), color, UserId);
                }
            }
        }
        void ProcessUSPCommand(Excutor excutor, string command, string[] args) {
            var servers = router.servers;
            var clientId = excutor.UserId;

            bool success = true;

            switch (command) {
                case "connect":
                case "warp": {
                        if (!excutor.IsClient) {
                            excutor.Chat("You can't use this command in console.", Color.Orange);
                            break;
                        }

                        var currentServer = excutor.TriggerServer;
                        var target = FindServer(args[0]);

                        if (target is null) {
                            currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral($"Server '{args[0]}' not found."), Color.Orange, clientId);
                            break;
                        }

                        if (ReferenceEquals(target, currentServer)) {
                            currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral("You are already on this server."), Color.Orange, clientId);
                            break;
                        }

                        router.ServerWarp(clientId, target);
                        break;
                    }
                case "current":
                case "servers":
                case "list": {
                        excutor.Chat("Server List: ", Color.Yellow);
                        for (int i = 0; i < servers.Length; i++) {
                            var server = servers[i];
                            if (!server.IsRunning) {
                                continue;
                            }
                            excutor.Chat($"{i + 1}: {server.Name}", Color.Yellow);
                        }
                        if (excutor.TriggerServer is not null) {
                            excutor.Chat($"Current Server: {excutor.TriggerServer.Name}", Color.Yellow);
                        }
                    }
                    break;
                case "players":
                case "playerlist":
                case "plys": {
                        if (excutor.TriggerServer is not null) {
                            var server = excutor.TriggerServer;
                            excutor.Chat($"Player in Server: {server.Name}", Color.Yellow);
                            for (int i = 0; i < Terraria.Main.maxPlayers; i++) {
                                if (server.Main.player[i].active) {
                                    excutor.Chat($"{i}: {server.Main.player[i].name}", Color.Yellow);
                                }
                            }
                        }
                        else {
                            foreach (var server in servers) {
                                if (!server.IsRunning) {
                                    continue;
                                }
                                excutor.Chat($"Player in Server: {server.Name}", Color.Yellow);
                                for (int i = 0; i < Terraria.Main.maxPlayers; i++) {
                                    if (server.Main.player[i].active) {
                                        excutor.Chat($"{i}: {server.Main.player[i].name}", Color.Yellow);
                                    }
                                }
                            }
                        }
                    }
                    break;
                case "?":
                case "help": {
                        excutor.Chat("Unified Server Process is a framework developed through secondary modifications to OTAPI, enabling", Color.Yellow);
                        excutor.Chat("multiple Terraria server logic instances to run in parallel as threads within a single process. ", Color.Yellow);
                        excutor.Chat("This project is currently in the alpha development stage, and the released program serves as a", Color.Yellow);
                        excutor.Chat("proof-of-concept demonstration for running this framework.", Color.Yellow);
                        excutor.Chat(" ", Color.Yellow);
                        excutor.Chat($"Current version: {Assembly.GetExecutingAssembly().GetName().Version}", Color.Yellow);
                        excutor.Chat("Usable Commands: ", Color.Yellow);
                        excutor.Chat("- Show server list: /current, /servers, /list", Color.Yellow);
                        excutor.Chat("- Show player list: /players, /playerlist, /plys", Color.Yellow);
                        if (excutor.IsServer) {
                            excutor.Chat("- Warp to a server (Player Only) : /connect <name | ID>, /warp <name | ID>", Color.Orange);
                        }
                        else {
                            excutor.Chat("- Warp to a server: /connect <name | ID>, /warp <name | ID>", Color.Yellow);
                        }
                    }
                    break;
                default:
                    success = false;
                    break;
            }

            if (excutor.IsClient) {
                var server = excutor.TriggerServer;
                if (success) {
                    server.Console.WriteLine($"Player '{server.Main.player[excutor.UserId].name}' executed command: '/{command}'", Color.Purple);
                }
            }
        }
    }
}
