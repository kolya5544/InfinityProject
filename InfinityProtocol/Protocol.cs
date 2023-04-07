using System;
using System.Net.Sockets;
using System.Text;
using letterCompression;

namespace InfinityProtocol
{
    public class Protocol
    {
        public static Packet GetPacket(NetworkStream ns)
        {
            Packet packet = new Packet();
            var buffer = new byte[1024];
            ns.Read(buffer, 0, 1);
            packet.id = buffer[0];

            if (packet.id == 0) throw new Exception("Incorrect ID.");

            ns.Read(buffer, 0, 1);
            packet.compression = buffer[0];
            buffer[0] = 0; buffer[1] = 0;
            ns.Read(buffer, 0, 2);
            packet.length = BitConverter.ToUInt16(buffer, 0);
            if (packet.length > buffer.Length)
            {
                buffer = new byte[packet.length];
            }
            int total = 0;
            packet.content = new byte[packet.length];
            while (total < packet.length)
            {
                int read = ns.Read(buffer, 0, packet.length);
                Array.Copy(buffer, 0, packet.content, total, read);
                total += read;
            }

            if (packet.compression == 1)
            {
                packet.s_cont = Letter.Decompress(packet.content);
            }

            return packet;
        }

        public static void SendPacket(NetworkStream ns, Packet p)
        {
            if (p.IsCompressable()) p.Compress();
            ns.Write(p.final, 0, p.final.Length);
        }
    }

    public class Packet
    {
        public enum UserPacket
        {
            LOGIN = 100,  // string
            MESSAGE = 101, // string
            PING = 102,
            TX_OFFER = 103,
            TX_ACCEPT = 104,
            TX_SEND = 105,
            TX_KILL = 106
        }

        public enum ServerPacket
        {
            OK = 1,
            ERROR = 2,
            NEW_MSG = 3,
            META = 4,
            MOTD = 5,
            ONLINE = 6,
            TX_OFFER = 7,
            TX_ACCEPT = 8,
            TX_SEND = 9,
            TX_ASSIGN = 10,
            TX_KILL = 11,
            JOIN = 12,
            LEAVE = 13
        }

        public byte id = 0; // 1 byte - ID of packet
        public byte compression = 0; // 1 byte - compression present (yes = 1, no = 0). We use Letter compression.
        public ushort length = 0; // 2 bytes - length of packet (0 to 65535)
        public byte[] content = null; // length bytes - packet contents

        public string s_cont { get { return Encoding.UTF8.GetString(content); } set { content = Encoding.UTF8.GetBytes(value); length = (ushort)content.Length; } }

        public byte[] final
        {
            get
            {
                var b = new byte[sizeof(byte) + sizeof(byte) + sizeof(ushort) + length];
                Array.Copy(BitConverter.GetBytes(id), b, 1);
                Array.Copy(BitConverter.GetBytes(compression), 0, b, 1, 1);
                Array.Copy(BitConverter.GetBytes(length), 0, b, 2, 2);
                Array.Copy(content, 0, b, 4, length);
                return b;
            }
        }

        public void Compress()
        {
            if (compression == 1) return;

            var v = s_cont;
            content = Letter.Compress(v);
            length = (ushort)content.Length;
            compression = 1;
        }

        public bool IsCompressable()
        {
            string v = s_cont;

            foreach (char c in v)
            {
                if (!Letter.inputAlpha.Contains(c)) return false;
            }
            return true;
        }
    }

    public class PBuild
    {
        public static Packet S_Success(string msg)
        {
            return new Packet()
            {
                s_cont = msg,
                id = (int)Packet.ServerPacket.OK
            };
        }

        public static Packet S_Error(string msg)
        {
            return new Packet()
            {
                s_cont = msg,
                id = (int)Packet.ServerPacket.ERROR
            };
        }

        public static Packet S_NewMessage(string msg, string sender)
        {
            return new Packet()
            {
                s_cont = $"{sender} {msg}",
                id = (int)Packet.ServerPacket.NEW_MSG
            };
        }

        public static Packet U_Auth(string nickname)
        {
            return new Packet()
            {
                s_cont = $"{nickname}",
                id = (int)Packet.UserPacket.LOGIN
            };
        }

        public static Packet S_Meta(string serverName)
        {
            return new Packet()
            {
                s_cont = $"{serverName}",
                id = (int)Packet.ServerPacket.META
            };
        }

        public static Packet S_MOTD(string motd)
        {
            return new Packet()
            {
                s_cont = $"{motd}",
                id = (int)Packet.ServerPacket.MOTD
            };
        }

        public static Packet S_Online(string[] online)
        {
            return new Packet()
            {
                s_cont = $"{online.Join(0)}",
                id = (int)Packet.ServerPacket.ONLINE
            };
        }

        public static Packet U_SendMessage(string msg)
        {
            return new Packet()
            {
                s_cont = $"{msg}",
                id = (int)Packet.UserPacket.MESSAGE
            };
        }

        public static Packet U_Ping()
        {
            return new Packet()
            {
                s_cont = $"PING",
                id = (int)Packet.UserPacket.PING
            };
        }

        public static Packet S_TXOffer(Request r)
        {
            return new Packet()
            {
                s_cont = $"{r.UID} {r.master}",
                id = (int)Packet.ServerPacket.TX_OFFER
            };
        }

        public static Packet S_TXAccept(Request r)
        {
            return new Packet()
            {
                s_cont = $"{r.UID}",
                id = (int)Packet.ServerPacket.TX_ACCEPT
            };
        }

        public static Packet S_TXAssign(Request r)
        {
            return new Packet()
            {
                s_cont = $"{r.master} {r.slave} {r.UID} {(r.Accepted ? 1 : 0)}",
                id = (int)Packet.ServerPacket.TX_ASSIGN
            };
        }

        public static Packet S_TXKill(Request r)
        {
            return new Packet()
            {
                s_cont = $"{r.UID}",
                id = (int)Packet.ServerPacket.TX_KILL
            };
        }

        public static Packet S_TXSend(Request r, string cont)
        {
            return new Packet()
            {
                s_cont = $"{r.UID} {cont}",
                id = (int)Packet.ServerPacket.TX_SEND
            };
        }

        public static Packet U_TXAccept(Request r)
        {
            return new Packet()
            {
                s_cont = $"{r.UID}",
                id = (int)Packet.UserPacket.TX_ACCEPT
            };
        }

        public static Packet U_TXSend(Request r, string cont)
        {
            return new Packet()
            {
                s_cont = $"{r.UID} {cont}",
                id = (int)Packet.UserPacket.TX_SEND
            };
        }

        public static Packet U_TXKill(Request r)
        {
            return new Packet()
            {
                s_cont = $"{r.UID}",
                id = (int)Packet.UserPacket.TX_KILL
            };
        }

        public static Packet U_TXOffer(Request r)
        {
            return new Packet()
            {
                s_cont = $"{r.slave}",
                id = (int)Packet.UserPacket.TX_OFFER
            };
        }

        public static Packet S_Join(string who)
        {
            return new Packet()
            {
                s_cont = $"{who}",
                id = (int)Packet.ServerPacket.JOIN
            };
        }

        public static Packet S_Leave(string who)
        {
            return new Packet()
            {
                s_cont = $"{who}",
                id = (int)Packet.ServerPacket.LEAVE
            };
        }
    }

    public static class Ext
    {
        public static string Join(this string[] arr, int start, char delim = ' ', int length = -1)
        {
            string res = "";
            for (int i = start; i < (length == -1 ? arr.Length : Math.Min(arr.Length, length)); i++)
            {
                res += arr[i] + delim;
            }
            return res.TrimEnd(delim);
        }
    }

    public class Request
    {
        public string master; // request creator (A)
        public string slave; // request receiver (B)
        public string UID;
        public bool Accepted = false;

        public byte[] privateKey; // both sides have this

        public byte[] publicKey1; // sent by master over insecure channel
        public byte[] publicKey2; // ^

        public byte[] masterMix; // generated by master, sent over insecure channel
        public byte[] slaveMix;  // generated by slave, sent over insecure channel

        public byte[] agreedKey; // generated by both sides. The same.
        public byte[] keyHash; // used to make sure no screw-ups had happened.
    }
}
