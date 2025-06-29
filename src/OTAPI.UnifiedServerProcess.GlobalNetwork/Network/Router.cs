using OTAPI.UnifiedServerProcess.GlobalNetwork.Servers;
using System.Net;
using System.Net.Sockets;
using Terraria;
using Terraria.Localization;
using Terraria.Net.Sockets;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork.Network
{
    public class Router
    {
        public int ListenPort { get; private set; }
        public static readonly RemoteClient[] globalClients = new RemoteClient[256];
        public static readonly MessageBuffer[] globalMsgBuffers = new MessageBuffer[257];
        readonly ServerContext[] clientCurrentlyServers = new ServerContext[256];
        public ServerContext GetClientCurrentlyServer(int clientIndex) => Volatile.Read(ref clientCurrentlyServers[clientIndex]);
        public void SetClientCurrentlyServer(int clientIndex, ServerContext server) => Volatile.Write(ref clientCurrentlyServers[clientIndex], server);

        static Router() {
            for (int i = 0; i < 256; i++) {
                globalClients[i] = new RemoteClient() {
                    Id = i,
                    ReadBuffer = new byte[1024]
                };
            }
            for (int i = 0; i < 257; i++) {
                globalMsgBuffers[i] = new MessageBuffer() {
                    whoAmI = i
                };
            }
        }
        public Router(int listenPort, ServerContext main, params ServerContext[] allServers) {
            ListenPort = listenPort;
            Array.Fill(clientCurrentlyServers, main);

            On.Terraria.NetMessageSystemContext.mfwh_CheckBytes += ProcessBytes;
            On.Terraria.NetplaySystemContext.mfwh_UpdateServerInMainThread += UpdateServerInMainThread;
            On.Terraria.NetMessageSystemContext.CheckCanSend += NetMessageSystemContext_CheckCanSend;

            this.main = main;
            this.servers = allServers;

            listener = new TcpListener(IPAddress.Any, listenPort);
            broadcastClient = new UdpClient();
            broadcastClient.EnableBroadcast = true;

            var listenThread = new Thread(ServerLoop) {
                IsBackground = true,
                Name = "Server Thread"
            };
            listenThread.Start();
        }

        private bool NetMessageSystemContext_CheckCanSend(On.Terraria.NetMessageSystemContext.orig_CheckCanSend orig, NetMessageSystemContext self, int clientIndex) {
            return GetClientCurrentlyServer(clientIndex) == self.root && globalClients[clientIndex].IsConnected();
        }

        private void UpdateServerInMainThread(On.Terraria.NetplaySystemContext.orig_mfwh_UpdateServerInMainThread orig, NetplaySystemContext self) {
            var server = self.root;
            for (int i = 0; i < 256; i++) {
                if (server != GetClientCurrentlyServer(i)) {
                    continue;
                }
                if (server.NetMessage.buffer[i].checkBytes) {
                    server.NetMessage.CheckBytes(i);
                }
            }
        }

        private void ProcessBytes(On.Terraria.NetMessageSystemContext.orig_mfwh_CheckBytes _, NetMessageSystemContext netMsg, int clientIndex) {
            var server = netMsg.root;
            var buffer = globalMsgBuffers[clientIndex];
            lock (buffer) {
                if (server.Main.dedServ && server.Netplay.Clients[clientIndex].PendingTermination) {
                    server.Netplay.Clients[clientIndex].PendingTerminationApproved = true;
                    buffer.checkBytes = false;
                    return;
                }
                int readPosition = 0;
                int unreadLength = buffer.totalData;
                try {
                    while (unreadLength >= 2) {
                        int packetLength = BitConverter.ToUInt16(buffer.readBuffer, readPosition);
                        if (unreadLength >= packetLength && packetLength != 0) {
                            long position = buffer.reader.BaseStream.Position;
                            buffer.GetData(server, readPosition + 2, packetLength - 2, out var _);

                            if (server.Main.dedServ && server.Netplay.Clients[clientIndex].PendingTermination) {
                                server.Netplay.Clients[clientIndex].PendingTerminationApproved = true;
                                buffer.checkBytes = false;
                                break;
                            }

                            buffer.reader.BaseStream.Position = position + packetLength;
                            unreadLength -= packetLength;
                            readPosition += packetLength;

                            if (GetClientCurrentlyServer(clientIndex) != server) {
                                // If there is unprocessed data remaining, copy it to the beginning of the buffer for the next processing round.
                                // Update buffer.totalData with the remaining byte count to prevent reprocessing of this data.
                                for (int i = 0; i < unreadLength; i++) {
                                    buffer.readBuffer[i] = buffer.readBuffer[readPosition + i];
                                }
                                buffer.totalData = unreadLength;
                                // Make sure remaining data will be processed in the next round on the next server
                                buffer.checkBytes = true;
                                return;
                            }

                            continue;
                        }
                        break;
                    }
                }
                catch (Exception exception) {
                    if (server.Main.dedServ && readPosition < globalMsgBuffers.Length - 100) {
                        server.Console.WriteLine(Language.GetTextValue("Error.NetMessageError", globalMsgBuffers[readPosition + 2]));
                    }
                    unreadLength = 0;
                    readPosition = 0;
                    server.Hooks.NetMessage.InvokeCheckBytesException(exception);
                }
                if (unreadLength != buffer.totalData) {
                    for (int i = 0; i < unreadLength; i++) {
                        buffer.readBuffer[i] = buffer.readBuffer[i + readPosition];
                    }
                    buffer.totalData = unreadLength;
                }
                buffer.checkBytes = false;
            }
        }

        public readonly ServerContext main;
        public readonly ServerContext[] servers;

        readonly TcpListener listener;
        volatile bool isListening;
        readonly UdpClient broadcastClient;
        volatile bool keepBroadcasting;

        public event Func<TcpClient, ISocket>? CreateSocket;
        public event Action? Started;


        void ServerLoop() {
            while (!main.IsRunning) {
                Thread.Sleep(10);
            }
            int sleepStep = 0;
            Started?.Invoke();
            while (true) {
                StartListeningIfNeeded();
                UpdateConnectedClients();
                sleepStep = (sleepStep + 1) % 10;
                Thread.Sleep(sleepStep == 0 ? 1 : 0);
            }
        }

        private void UpdateConnectedClients() {
            try {
                for (int i = 0; i < 256; i++) {
                    var client = globalClients[i];
                    var server = GetClientCurrentlyServer(i);

                    if (client.PendingTermination) {
                        if (client.PendingTerminationApproved) {
                            client.Reset(main);
                            server.NetMessage.SyncDisconnectedPlayer(i);

                            bool active = server.Main.player[i].active;
                            server.Main.player[i].active = false;
                            if (active) {
                                server.Player.Hooks.PlayerDisconnect(i);
                            }

                            SetClientCurrentlyServer(i, main);
                        }
                        continue;
                    }
                    if (client.IsConnected()) {
                        lock (client) {
                            client.Update(server);
                            server.Netplay.HasClients = true;
                        }
                        continue;
                    }
                    if (client.IsActive) {
                        client.PendingTermination = true;
                        client.PendingTerminationApproved = true;
                        continue;
                    }
                    client.StatusText2 = "";
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex);
            }
        }

        static int GetClientSpace() {
            int space = 255;
            for (int i = 0; i < 255; i++) {
                if (globalClients[i].IsActive) {
                    space -= 1;
                }
            }
            return space;
        }
        static int GetActiveClientCount() {
            int count = 0;
            for (int i = 0; i < 255; i++) {
                if (globalClients[i].IsActive) {
                    count += 1;
                }
            }
            return count;
        }

        void StartListeningIfNeeded() {
            if (isListening || !main.IsRunning || GetClientSpace() <= 0) {
                return;
            }
            isListening = true;
            listener.Start();
            Task.Run(ListenLoop);
            Task.Run(LaunchBroadcast);
        }
        void ListenLoop() {
            while (main.IsRunning && GetClientSpace() > 0) {
                try {
                    var client = listener.AcceptTcpClient();
                    var socket = CreateSocket?.Invoke(client) ?? new TcpSocket(client);
                    OnConnectionAccepted(socket);
                }
                catch {
                }
            }
            listener.Stop();
            isListening = false;
            keepBroadcasting = false;
        }
        void LaunchBroadcast() {
            try {
                keepBroadcasting = true;
                int playerCountPosInStream = 0;
                byte[] data;
                using (MemoryStream memoryStream = new MemoryStream()) {
                    using (BinaryWriter bw = new BinaryWriter(memoryStream)) {
                        int value = 1010;
                        bw.Write(value);
                        bw.Write(ListenPort);
                        bw.Write("Unified-Server-Process");
                        string text = Dns.GetHostName();
                        if (text == "localhost") {
                            text = Environment.MachineName;
                        }
                        bw.Write(text);
                        bw.Write((ushort)main.Main.maxTilesX);
                        bw.Write(main.Main.ActiveWorldFileData.HasCrimson);
                        bw.Write(main.Main.ActiveWorldFileData.GameMode);
                        bw.Write(255);
                        playerCountPosInStream = (int)memoryStream.Position;
                        bw.Write((byte)0);
                        bw.Write(main.Main.ActiveWorldFileData.IsHardMode);
                        bw.Flush();
                        data = memoryStream.ToArray();
                    }
                }
                do {
                    data[(int)playerCountPosInStream] = (byte)GetActiveClientCount();
                    try {
                        broadcastClient.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, 8888));
                    }
                    catch {
                    }
                    Thread.Sleep(1000);
                }
                while (keepBroadcasting);
            }
            catch { 
                keepBroadcasting = false;
            }
        }

        void OnConnectionAccepted(ISocket client) {
            int id = FindNextEmptyClientSlot();
            if (id != -1) {
                SetClientCurrentlyServer(id, main);
                globalClients[id].Reset(main);
                globalClients[id].Socket = client;
            }
            else {
                lock (main.Netplay.fullBuffer) {
                    main.Netplay.KickClient(client, NetworkText.FromKey("CLI.ServerIsFull"));
                }
            }
            if (FindNextEmptyClientSlot() == -1) {
                listener.Stop();
                isListening = false;
            }
        }
        int FindNextEmptyClientSlot() {
            for (int i = 0; i < main.Main.maxNetPlayers; i++) {
                if (!globalClients[i].IsConnected()) {
                    return i;
                }
            }
            return -1;
        }

        public void ServerWarp(byte plr, ServerContext to) {
            var from = GetClientCurrentlyServer(plr);

            if (from == to) {
                return;
            }
            if (!to.IsRunning) {
                return;
            }

            var client = globalClients[plr];
            lock (client) {
                SyncHelper.SyncPlayerLeaveToOthers(from, plr);
                SyncHelper.SyncServerOfflineToPlayer(from, plr);
                from.Console.WriteLine($"[USP] Player '{from.Main.player[plr].name}' warped to {to.Name}, current players: {to.NPC.GetActivePlayerCount()}");

                var inactivePlayer = to.Main.player[plr];
                var activePlayer = from.Main.player[plr];

                from.Main.player[plr] = inactivePlayer;
                to.Main.player[plr] = activePlayer;

                inactivePlayer.active = false;
                activePlayer.active = true;

                SetClientCurrentlyServer(plr, to);
                client.ResetSections(to);

                SyncHelper.SyncServerOnlineToPlayer(to, plr);
                SyncHelper.SyncPlayerJoinToOthers(to, plr);
                to.Console.WriteLine($"[USP] Player '{to.Main.player[plr].name}' joined from {from.Name}, current players: {to.NPC.GetActivePlayerCount()}");
            }
        }
    }
}
