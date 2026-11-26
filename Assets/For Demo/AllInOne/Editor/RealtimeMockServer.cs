using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>
    /// A small WebSocket server on this computer that stands in for the Realtime API in automated tests. It keeps every
    /// message of the client and sends what the test tells it to.
    /// </summary>
    internal sealed class RealtimeMockServer : IDisposable
    {
        private const string AcceptSuffix = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const int TextFrame = 1;
        private const int CloseFrame = 8;
        private const int FirstControlFrame = 8;

        private readonly TcpListener listener;
        private readonly List<JObject> received = new List<JObject>();
        private readonly object sync = new object();
        private readonly object writing = new object();
        private readonly BlockingCollection<byte[]> outbox = new BlockingCollection<byte[]>();
        private TcpClient client;
        private NetworkStream stream;
        private volatile bool isDisposed;
        private int connections;

        public RealtimeMockServer()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            new Thread(Accept) { IsBackground = true }.Start();
            new Thread(SendOutbox) { IsBackground = true }.Start();
        }

        public string Url => "ws://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/v1/realtime";

        /// <summary>How many connections were opened so far.</summary>
        public int Connections => connections;

        /// <summary>The close of the client is answered this much later.</summary>
        public int CloseDelayMilliseconds { get; set; }

        /// <summary>When false, new connections are refused.</summary>
        public bool AcceptsConnections { get; set; } = true;

        /// <summary>When true, a connection is closed as soon as it is open.</summary>
        public bool ClosesAtOnce { get; set; }

        /// <summary>How many messages of the client have this type.</summary>
        public int Count(string type)
        {
            lock (sync) return received.FindAll(m => (string)m["type"] == type).Count;
        }

        /// <summary>The last message of this type, or null.</summary>
        public JObject Last(string type)
        {
            lock (sync) return received.FindLast(m => (string)m["type"] == type);
        }

        /// <summary>All messages so far, in order.</summary>
        public JObject[] All()
        {
            lock (sync) return received.ToArray();
        }

        public void Forget()
        {
            lock (sync) received.Clear();
        }

        /// <summary>Sends a message in the background, because the client reads on the main thread.</summary>
        public void Send(JObject message) => outbox.Add(Encoding.UTF8.GetBytes(message.ToString(Formatting.None)));

        /// <summary>Drops the connection without a close message, like a lost network.</summary>
        public void Drop()
        {
            TcpClient current = client;
            client = null;
            current?.Close();
        }

        public void Dispose()
        {
            isDisposed = true;
            outbox.CompleteAdding();
            Drop();
            listener.Stop();
        }

        private void Accept()
        {
            while (!isDisposed)
            {
                try
                {
                    TcpClient next = listener.AcceptTcpClient();
                    if (!AcceptsConnections)
                    {
                        next.Close();
                        continue;
                    }
                    new Thread(() => Serve(next)) { IsBackground = true }.Start();
                }
                catch (Exception) when (isDisposed)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
            }
        }

        private void Serve(TcpClient connection)
        {
            try
            {
                NetworkStream connectionStream = connection.GetStream();
                Shake(connectionStream);
                client = connection;
                stream = connectionStream;
                Interlocked.Increment(ref connections);
                if (ClosesAtOnce)
                {
                    WriteTo(connectionStream, CloseFrame, new byte[0]);
                    return;
                }
                Send(new JObject { ["type"] = "session.created" });
                while (ReadMessage(connection, connectionStream)) { }
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is SocketException)
            {
                // The connection has ended.
            }
            finally
            {
                connection.Close();
            }
        }

        private static void Shake(NetworkStream connection)
        {
            var request = new StringBuilder();
            while (!request.ToString().EndsWith("\r\n\r\n"))
            {
                int next = connection.ReadByte();
                if (next < 0) throw new IOException("closed");
                request.Append((char)next);
            }
            string key = Regex.Match(request.ToString(), "Sec-WebSocket-Key: (.*)\r\n", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            string accept;
            using (SHA1 sha = SHA1.Create()) accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + AcceptSuffix)));
            byte[] answer = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
            connection.Write(answer, 0, answer.Length);
        }

        private bool ReadMessage(TcpClient connection, NetworkStream from)
        {
            var message = new MemoryStream();
            while (true)
            {
                byte[] head = Read(from, 2);
                int code = head[0] & 15;
                long length = head[1] & 127;
                if (length == 126) length = Big(Read(from, 2));
                else if (length == 127) length = Big(Read(from, 8));
                byte[] mask = Read(from, 4);
                byte[] data = Read(from, (int)length);
                for (int i = 0; i < data.Length; i++) data[i] ^= mask[i % 4];

                if (code == CloseFrame)
                {
                    if (CloseDelayMilliseconds > 0) Thread.Sleep(CloseDelayMilliseconds);
                    WriteTo(from, CloseFrame, data);
                    return false;
                }
                if (code >= FirstControlFrame) continue;
                message.Write(data, 0, data.Length);
                if ((head[0] & 128) == 0) continue;

                JObject parsed = JObject.Parse(Encoding.UTF8.GetString(message.ToArray()));
                lock (sync) received.Add(parsed);
                if ((string)parsed["type"] == "session.update") WriteTo(from, TextFrame, Encoding.UTF8.GetBytes("{\"type\":\"session.updated\"}"));
                return true;
            }
        }

        private static byte[] Read(NetworkStream from, int count)
        {
            var data = new byte[count];
            for (int done = 0; done < count;)
            {
                int n = from.Read(data, done, count - done);
                if (n <= 0) throw new IOException("closed");
                done += n;
            }
            return data;
        }

        private static long Big(byte[] bytes)
        {
            long value = 0;
            foreach (byte b in bytes) value = value << 8 | b;
            return value;
        }

        private void SendOutbox()
        {
            foreach (byte[] data in outbox.GetConsumingEnumerable())
            {
                NetworkStream to = stream;
                if (client == null || to == null) continue;
                try
                {
                    WriteTo(to, TextFrame, data);
                }
                catch (Exception e) when (e is IOException || e is ObjectDisposedException)
                {
                    // The client has gone.
                }
            }
        }

        private void WriteTo(NetworkStream to, int code, byte[] data)
        {
            var frame = new MemoryStream();
            frame.WriteByte((byte)(128 | code));
            if (data.Length < 126) frame.WriteByte((byte)data.Length);
            else
            {
                bool isLong = data.Length > ushort.MaxValue;
                frame.WriteByte((byte)(isLong ? 127 : 126));
                for (int shift = isLong ? 56 : 8; shift >= 0; shift -= 8) frame.WriteByte((byte)((long)data.Length >> shift));
            }
            frame.Write(data, 0, data.Length);
            byte[] bytes = frame.ToArray();
            lock (writing) to.Write(bytes, 0, bytes.Length);
        }
    }
}