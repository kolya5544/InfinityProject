using InfinityServer;
using System;
using System.Net.Sockets;
using System.Net;
using System.Text.RegularExpressions;
using InfinityProtocol;

namespace InfinityServer
{
    public class Program
    {
        public static Config cfg = Config.LoadConfig("cfg.json");
        public static List<User> users = new List<User>();
        public static List<Request> TX = new List<Request>();

        public static Random rng = new Random();

        public static void Log(string msg)
        {
            string s = $"[{DateTime.UtcNow:HH:mm:ss}] {msg}";
            Console.WriteLine(s);
        }

        public static void Main(string[] args)
        {
            letterCompression.Letter.Init();

            string[] reserved = new string[] { "SYSTEM", "MOTD", "ERROR" };

            Log($"Starting the server at {cfg.hostname}:{cfg.port}...");
            var tcp = new TcpListener(IPAddress.Parse(cfg.hostname), cfg.port);
            tcp.Start();
            Log("Listening for new connections!");
            while (true)
            {
                var client = tcp.AcceptTcpClient();

                string hostname = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

                Log($"Got new connection from {hostname}!");

                new Thread(() =>
                {
                    var ns = client.GetStream();
                    ns.ReadTimeout = 1000;
                    ns.WriteTimeout = 1000;

                    var user = new User();
                    user.ns = ns;
                    users.Add(user);

                    Protocol.SendPacket(ns, PBuild.S_Meta(cfg.serverName));
                    Protocol.SendPacket(ns, PBuild.S_MOTD(cfg.MOTD));

                    while (true)
                    {
                        try
                        {
                            var packet = Protocol.GetPacket(ns);
                            var id = (Packet.UserPacket)packet.id;


                            if (id == Packet.UserPacket.LOGIN && !user.Authed) // "nickname"
                            {
                                string nickname = packet.s_cont;

                                Log(nickname);

                                if (Regex.IsMatch(nickname, "^[A-Za-z0-9_]+$") && nickname.Length > 0 && nickname.Length < 24 && !users.Any(z => z.Nickname == nickname) && !reserved.Any(z => z == nickname))
                                {
                                    Protocol.SendPacket(ns, PBuild.S_Success($"Your nickname is now {nickname}!"));

                                    user.Nickname = nickname;
                                    user.Authed = true;
                                    ns.ReadTimeout = 10000;
                                    ns.WriteTimeout = 10000;

                                    users.ForEach(z =>
                                    {
                                        if (z.Authed)
                                        {
                                            Protocol.SendPacket(z.ns, PBuild.S_Online(GetOnline()));
                                            Protocol.SendPacket(z.ns, PBuild.S_Join(nickname));
                                        }
                                    });
                                } else
                                {
                                    Protocol.SendPacket(ns, PBuild.S_Error($"Your nickname is currently busy or doesn't match necessary conditions: should be 1-24 characters long, alphanumeric string. Underscores are allowed."));
                                }
                            } else if (user.Authed)
                            {
                                if (id == Packet.UserPacket.MESSAGE)
                                {
                                    string nickname = user.Nickname;
                                    string content = packet.s_cont;

                                    users.ForEach(z =>
                                    {
                                        if (z.Authed)
                                        {
                                            Protocol.SendPacket(z.ns, PBuild.S_NewMessage(content, nickname));
                                        }
                                    });
                                } else if (id == Packet.UserPacket.TX_OFFER) //sent by master
                                {
                                    string master = user.Nickname;
                                    string slave = packet.s_cont;

                                    var u = users.Find(z => z.Nickname == slave);

                                    if (u == null) { Protocol.SendPacket(ns, PBuild.S_Error("No user with such nickname found!")); continue; }
                                    if (TX.Exists(z => z.master == master && z.slave == slave)) { Protocol.SendPacket(ns, PBuild.S_Error("Such request had already been made!")); continue; }

                                    var req = new Request()
                                    {
                                        slave = slave,
                                        master = master,
                                        UID = GenUID()
                                    };
                                    TX.Add(req);

                                    Protocol.SendPacket(ns, PBuild.S_Success($"Request {req.UID} successfully sent to {req.slave}!"));
                                    Protocol.SendPacket(ns, PBuild.S_TXAssign(req));
                                    Protocol.SendPacket(u.ns, PBuild.S_TXOffer(req)); // forward the offer to slave
                                } else if (id == Packet.UserPacket.TX_ACCEPT) //sent by slave
                                {
                                    string uid = packet.s_cont;

                                    var u = TX.Find(z => z.UID == uid);

                                    if (u == null) { Protocol.SendPacket(ns, PBuild.S_Error("No request with such UID found!")); continue; }

                                    var mU = users.Find(z => z.Nickname == u.master);

                                    if (mU == null) { Protocol.SendPacket(ns, PBuild.S_Error("This request is no longer actual!")); continue; }

                                    u.Accepted = true;

                                    Protocol.SendPacket(ns, PBuild.S_Success($"Request {u.UID} by {u.master} successfully accepted!"));
                                    Protocol.SendPacket(ns, PBuild.S_TXAssign(u));
                                    Protocol.SendPacket(mU.ns, PBuild.S_TXAccept(u)); // forward the decision to master
                                } else if (id == Packet.UserPacket.TX_SEND) // can be sent by both sides, but...
                                {
                                    // usually, now DH algo takes place.
                                    // first, master sends "master <public key 1> <public key 2> <master mix>"
                                    // then, slave sends "slave <slave mix>". Both sides now calculate private key
                                    // then, slave sends "hash <hash>", and if hashes don't match, master should kill the session with TX_KILL
                                    // if they do match, however, sides can now use Flawless v2 to encrypt and send messages to each other.
                                    // they use "enc <encrypted message>" for that.

                                    // but really the only thing server does at this point is forward messages, so we don't care what to forward, lol

                                    string sender = user.Nickname;

                                    string[] sc = packet.s_cont.Split(' ');
                                    string uid = sc[0];
                                    string content = sc.Join(1);

                                    var u = TX.Find(z => z.UID == uid && (z.master == sender || z.slave == sender));

                                    if (u == null) { Protocol.SendPacket(ns, PBuild.S_Error("No request with such UID found!")); continue; }
                                    if (!u.Accepted) { Protocol.SendPacket(ns, PBuild.S_Error("This request wasn't accepted yet!")); continue; }

                                    var rec = users.Find(z => z.Nickname != sender && (z.Nickname == u.slave || z.Nickname == u.master));
                                    if (rec == null) { Protocol.SendPacket(ns, PBuild.S_Error("This request is no longer actual!")); continue; }

                                    Protocol.SendPacket(rec.ns, PBuild.S_TXSend(u, content));
                                } else if (id == Packet.UserPacket.TX_KILL)
                                {
                                    string sender = user.Nickname;

                                    string uid = packet.s_cont;

                                    var u = TX.Find(z => z.UID == uid && (z.master == sender || z.slave == sender));

                                    if (u == null) { Protocol.SendPacket(ns, PBuild.S_Error("No request with such UID found!")); continue; }
                                    if (!u.Accepted) { Protocol.SendPacket(ns, PBuild.S_Error("This request wasn't accepted yet!")); continue; }

                                    var rec = users.Find(z => z.Nickname != sender && (z.Nickname == u.slave || z.Nickname == u.master));
                                    if (rec == null) { Protocol.SendPacket(ns, PBuild.S_Error("This request is no longer actual!")); continue; }

                                    Protocol.SendPacket(rec.ns, PBuild.S_TXKill(u));
                                    Protocol.SendPacket(ns, PBuild.S_TXKill(u));
                                    TX.Remove(u);
                                }
                            }
                        } catch (Exception e)
                        {
                            Log($"Client {hostname} disconnected!");
                            users.Remove(user);
                            if (user.Authed)
                            {
                                TX.RemoveAll(z => z.master == user.Nickname || z.slave == user.Nickname);
                                users.ForEach(z =>
                                {
                                    if (z.Authed)
                                    {
                                        Protocol.SendPacket(z.ns, PBuild.S_Online(GetOnline()));
                                        Protocol.SendPacket(z.ns, PBuild.S_Leave(user.Nickname));
                                    }
                                });
                            }
                            break;
                        }
                    }
                }).Start();
            }
        }

        private static string GenUID()
        {
            return rng.Next(1000000000, int.MaxValue).ToString("X2");
        }

        private static string[] GetOnline()
        {
            List<string> online = new();
            users.ForEach(z =>
            {
                if (z.Authed)
                {
                    online.Add(z.Nickname);
                }
            });
            return online.ToArray();
        }
    }
}