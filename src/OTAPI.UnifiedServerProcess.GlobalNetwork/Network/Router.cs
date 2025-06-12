using OTAPI.UnifiedServerProcess.GlobalNetwork.Servers;
using System.Net;
using System.Net.Sockets;
using Terraria.Net.Sockets;
using Terraria;
using UnifiedServerProcess;
using Terraria.Localization;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork.Network
{
    public class Router
    {
        public Router(int listenPort, ServerContext main, params ServerContext[] allServers) {
            On.Terraria.Chat.Commands.SayChatCommand.ProcessIncomingMessage += ProcessIncomingMessage;

            this.main = main;
            this.servers = allServers;

            listener = new TcpListener(IPAddress.Any, listenPort);

            var listenThread = new Thread(ServerLoop) {
                IsBackground = true,
                Name = "Server Thread"
            };
            listenThread.Start();
        }

        public readonly ServerContext main;
        public readonly ServerContext[] servers;

        readonly TcpListener listener;
        volatile bool isListening;
        void ServerLoop() {
            while (true) { 
                StartListeningIfNeeded();
                foreach (var server in servers) {
                    if (server.IsRunning) {
                        server.Netplay.UpdateConnectedClients();
                    }
                }
            }
        }
        void StartListeningIfNeeded() {
            if (isListening || !main.IsRunning || main.GetClientSpace() <= 0) {
                return;
            }
            isListening = true;
            listener.Start();
            Task.Run(ListenLoop);
        }
        void ListenLoop() {
            while (main.IsRunning && main.GetClientSpace() > 0) {
                try {
                    var client = listener.AcceptTcpClient();
                    var socket = CreateSocket?.Invoke(client) ?? new TcpSocket(client);
                    main.Netplay.OnConnectionAccepted(socket);
                }
                catch {
                }
            }
            listener.Stop();
            isListening = false;
        }
        public event Func<TcpClient, ISocket>? CreateSocket;
        void ProcessIncomingMessage(On.Terraria.Chat.Commands.SayChatCommand.orig_ProcessIncomingMessage orig, Terraria.Chat.Commands.SayChatCommand self,
            RootContext root, string text, byte clientId) {

            if (ProcessUSPCommandText(root, text, clientId)) {
                return;
            }

            orig(self, root, text, clientId);
        }
        bool ProcessUSPCommandText(RootContext root, string text, byte clientId) {
            if (root is not ServerContext currentServer) {
                return false;
            }
            text = text.Trim();

            if (!text.StartsWith('/')) {
                return false;
            }

            var textArgs = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = textArgs[0].TrimStart('/');
            var args = textArgs.Skip(1).ToArray();

            ProcessUSPCommand(currentServer, clientId, command, args);
            return true;
        }

        ServerContext? FindServer(string name) {
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
        void ProcessUSPCommand(ServerContext currentServer, byte clientId, string command, string[] args) {
            switch (command) {
                case "connect":
                case "warp": {
                        var target = FindServer(args[0]);

                        if (target is null) {
                            currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral($"Server '{args[0]}' not found"), Color.Orange, clientId);
                            return;
                        }

                        if (ReferenceEquals(target, currentServer)) {
                            currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral("You are already on this server."), Color.Orange, clientId);
                            return;
                        }

                        ServerWarp(clientId, currentServer, target);
                        return;
                    }
                case "current":
                case "servers":
                case "list": {
                        currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral($"Server List: "), Color.Yellow, clientId);
                        for (int i = 0; i < servers.Length; i++) {
                            var server = servers[i];
                            if (!server.IsRunning) {
                                continue;
                            }
                            currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral($"{i + 1}: {server.Name}"), Color.Yellow, clientId);
                        }
                        currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral($"Current Server: {currentServer.Name}"), Color.Yellow, clientId);
                    }
                    break;
                case "?":
                case "help": {
                        currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral("Unified Server Process is a program that allows multiple Terraria servers running on the same process."), Color.Yellow, clientId);
                        currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral($"Current version: {Assembly.GetExecutingAssembly().GetName().Version}"), Color.Yellow, clientId);
                        currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral("Usable Commands: "), Color.Yellow, clientId);
                        currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral("- Show server list: /current, /servers, /list"), Color.Yellow, clientId);
                        currentServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral("- Warp to a server: /connect <name|id>, /warp <name|id>"), Color.Yellow, clientId);
                    }
                    break;
            }
        }

        public static void ServerWarp(byte client, ServerContext from, ServerContext to) {
            lock (from.Netplay.fullBuffer) {
                var player = from.Main.player[client];
                var buffer = from.NetMessage.buffer[client];
                var remote = from.Netplay.Clients[client];

                var packed = new ClientPackedData(player, remote, buffer);

                if (to.TryAcceptClient(packed)) {
                    from.Main.player[client] = new Player(from) {
                        whoAmI = client,
                    };
                    from.NetMessage.buffer[client] = new MessageBuffer() {
                        whoAmI = client,
                    };
                    from.Netplay.Clients[client] = new RemoteClient(from) {
                        Id = client,
                        ReadBuffer = new byte[1024],
                    };
                    from.Netplay.Clients[client].Reset(from);

                    // If there is unprocessed data remaining, copy it to the beginning of the buffer for the next processing round.
                    // Update buffer.totalData with the remaining byte count to prevent reprocessing of this data.
                    var restDataBytesCount = (int)(buffer.totalData - buffer.readerStream.Position);
                    for (int i = 0; i < restDataBytesCount; i++) {
                        buffer.readBuffer[i] = buffer.readBuffer[buffer.readerStream.Position + i];
                    }
                    buffer.totalData = restDataBytesCount;

                    from.ClientLeave(packed);
                }
            }
        }
    }
}
