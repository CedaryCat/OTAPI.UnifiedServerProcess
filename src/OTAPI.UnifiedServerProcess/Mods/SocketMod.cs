#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS0436 // Type conflicts with imported type
#nullable disable
using ModFramework;
using System;
using System.Buffers;
using System.Net.Sockets;
using Terraria.Net;
using Terraria.Net.Sockets;
using Terraria.Social;

[Modification(ModType.PreWrite, "Add Method to ISocket", ModPriority.Early)]
[MonoMod.MonoModIgnore]
void NetplayConnectionCheck(ModFwModder modder) {
    Console.WriteLine("Added method to ISocket");
}

namespace Terraria.Net.Sockets
{
    public interface ISocket
    {
        [MonoMod.MonoModIgnore]
        void Close();
        void AsyncSendNoCopy(byte[] data, int offset, int size, SocketSendCallback callback, object state = null);
        void AsyncSend(ReadOnlyMemory<byte> data, SocketSendCallback callback, object state = null);
    }
    public class TcpSocket : ISocket
    {
        [MonoMod.MonoModIgnore]
        void ISocket.Close() { }
        public TcpClient _connection;
        void ISocket.AsyncSendNoCopy(byte[] data, int offset, int size, SocketSendCallback callback, object state) {
            _connection.GetStream().BeginWrite(data, offset, size, static result => {
                var tuple = (Tuple<TcpSocket, SocketSendCallback, object>)result.AsyncState;
                try {
                    tuple.Item1._connection.GetStream().EndWrite(result);
                    tuple.Item2(tuple.Item3);
                }
                catch (Exception) {
                    ((ISocket)tuple.Item1).Close();
                }
            }, new Tuple<TcpSocket, SocketSendCallback, object>(this, callback, state));
        }
        void ISocket.AsyncSend(ReadOnlyMemory<byte> data, SocketSendCallback callback, object state) {
            var array = ArrayPool<byte>.Shared.Rent(data.Length);
            data.CopyTo(array);
            _connection.GetStream().BeginWrite(array, 0, data.Length, static result => {
                var tuple = (Tuple<TcpSocket, byte[], SocketSendCallback, object>)result.AsyncState;
                try {
                    tuple.Item1._connection.GetStream().EndWrite(result);
                    tuple.Item3(tuple.Item4);
                }
                catch (Exception) {
                    ((ISocket)tuple.Item1).Close();
                }
                finally {
                    ArrayPool<byte>.Shared.Return(tuple.Item2);
                }
            }, new Tuple<TcpSocket, byte[], SocketSendCallback, object>(this, array, callback, state));
        }
    }
    public class SocialSocket : ISocket
    {
        [MonoMod.MonoModIgnore]
        void ISocket.Close() { }
        public RemoteAddress _remoteAddress;
        void ISocket.AsyncSendNoCopy(byte[] data, int offset, int size, SocketSendCallback callback, object state) {
            if (offset is not 0) {
                var copy = ArrayPool<byte>.Shared.Rent(size);
                Buffer.BlockCopy(data, offset, copy, 0, size);
                SocialAPI.Network.Send(_remoteAddress, copy, size);
                ArrayPool<byte>.Shared.Return(copy);
                callback.BeginInvoke(state, null, null);
            }
            else {
                SocialAPI.Network.Send(_remoteAddress, data, size);
                callback.BeginInvoke(state, null, null);
            }
        }
        void ISocket.AsyncSend(ReadOnlyMemory<byte> data, SocketSendCallback callback, object state) {
            var copy = ArrayPool<byte>.Shared.Rent(data.Length);
            data.CopyTo(copy);
            SocialAPI.Network.Send(_remoteAddress, copy, data.Length);
            ArrayPool<byte>.Shared.Return(copy);
            callback.BeginInvoke(state, null, null);
        }
    }
}
