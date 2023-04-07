using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using InfinityProtocol;
using System.Threading;

namespace InfinityClient
{
    public partial class ChatWnd : Form
    {
        public static ChatWnd ins;

        public static List<Request> reqs = new List<Request>();
        public static Dictionary<Request, PMWnd> windows = new Dictionary<Request, PMWnd>();

        public static string hostname;
        public static string nickname;

        public static string serverName;
        public static string MOTD;

        public static NetworkStream ns = null;

        public void Log(string msg, string sender = "SYSTEM")
        {
            textBox1.Invoke((Action)(() => {
                textBox1.AppendText($"[{DateTimeOffset.Now:HH:mm:ss}] <{sender}> {msg}\r\n");
            }));
        }

        public ChatWnd()
        {
            InitializeComponent();
        }

        private void ChatWnd_Load(object sender, EventArgs e)
        {
            Log("Chat window initialized successfully.");
        }

        public void Connect()
        {
            Log($"Attempting to connect to {hostname}...");
            string host = hostname.Split(':')[0];
            int port = int.Parse(hostname.Split(':')[1]);
            TcpClient c = new TcpClient(host, port);
            ns = c.GetStream();
            var meta = Protocol.GetPacket(ns);
            var motd = Protocol.GetPacket(ns);
            serverName = meta.s_cont; MOTD = motd.s_cont;
            Log($"Connected! Authenication in progress...");
            Protocol.SendPacket(ns, PBuild.U_Auth(nickname));
            var packet = Protocol.GetPacket(ns);
            if (packet.id == (byte)Packet.ServerPacket.ERROR)
            {
                Log($"FATAL ERROR! Content: {packet.s_cont}"); ns = null; return;
            } else
            {
                Log($"You are now connected to: {serverName}");
                string[] m = MOTD.Split('\n');
                m.ToList().ForEach(z =>
                {
                    Log(z, "MOTD");
                });
            }
            new Thread(() => { HandleNewPackets(); }).Start();
            new Thread(() => { HandlePing(); }).Start();
        }

        private void HandlePing()
        {
            while (true)
            {
                Protocol.SendPacket(ns, PBuild.U_Ping());
                Thread.Sleep(5000);
            }
        }

        public void HandleNewPackets()
        {
            while (true)
            {
                var p = Protocol.GetPacket(ns);

                var id = (Packet.ServerPacket)p.id;

                if (id == Packet.ServerPacket.NEW_MSG)
                {
                    string[] sc = p.s_cont.Split(' ');
                    string sender = sc[0];
                    string msg = sc.Join(1);

                    Log(msg, sender);
                } else if (id == Packet.ServerPacket.ONLINE)
                {
                    string[] online = p.s_cont.Split(' ');
                    UpdateOnline(online);
                } else if (id == Packet.ServerPacket.TX_OFFER)
                {
                    Request r = new Request();
                    string[] sc = p.s_cont.Split(' ');
                    r.UID = sc[0];
                    r.master = sc[1];
                    r.slave = nickname;

                    reqs.Add(r);

                    Log($"An offer to establish direct chat from {r.master}! Write /accept {r.master} to accept");
                } else if (id == Packet.ServerPacket.TX_ASSIGN)
                {
                    string[] sc = p.s_cont.Split(' ');
                    string m = sc[0]; string slave = sc[1];
                    string uid = sc[2]; bool accepted = sc[3] == "1";
                    
                    var r = reqs.Find(z => z.master == m && z.slave == slave);
                    if (r == null) { continue; }

                    r.UID = uid;
                    r.Accepted = accepted;

                    if (r.Accepted)
                    {
                        var c = windows.ContainsKey(r);
                        if (!c)
                        {
                            var wnd = new PMWnd();
                            wnd.r = r;
                            Invoke((Action)(() =>
                            {
                                wnd.Show();
                            }));
                            windows.Add(r, wnd);
                        }
                    }
                } else if (id == Packet.ServerPacket.TX_SEND)
                {
                    string[] sc = p.s_cont.Split(' ');

                    string uid = sc[0];
                    string msg = sc.Join(1);

                    var r = reqs.Find(z => z.UID == uid && z.Accepted);
                    if (r == null) { continue; }

                    var wnd = windows[r];
                    
                    if (msg.StartsWith("master"))
                    {
                        string pkey1 = sc[2];
                        string pkey2 = sc[3];
                        string mmix = sc[4];

                        r.publicKey1 = Convert.FromBase64String(pkey1);
                        r.publicKey2 = Convert.FromBase64String(pkey2);
                        r.masterMix = Convert.FromBase64String(mmix);

                        wnd.Invoke((Action)(() =>
                        {
                            wnd.GotMasterMix();
                        }));
                    } else if (msg.StartsWith("slave"))
                    {
                        string slaveMix = sc[2];
                        r.slaveMix = Convert.FromBase64String(slaveMix);

                        wnd.Invoke((Action)(() =>
                        {
                            wnd.GotSlaveMix();
                        }));
                        
                    } else if (msg.StartsWith("hash"))
                    {
                        string hash = sc[2];

                        wnd.Invoke((Action)(() =>
                        {
                            wnd.GotHash(hash);
                        }));
                    } else
                    {
                        wnd.Invoke((Action)(() =>
                        {
                            wnd.GotMsg(msg);
                        }));
                    }
                } else if (id == Packet.ServerPacket.TX_ACCEPT)
                {
                    string uid = p.s_cont;

                    var r = reqs.Find(z => z.UID == uid);
                    if (r == null) { continue; }
                    r.Accepted = true;

                    var c = windows.ContainsKey(r);
                    if (!c)
                    {
                        var wnd = new PMWnd();
                        wnd.r = r;
                        Invoke((Action)(() =>
                        {
                            wnd.Show();
                        }));
                        windows.Add(r, wnd);
                    }
                } else if (id == Packet.ServerPacket.JOIN)
                {
                    string who = p.s_cont;

                    Log($"{who} had joined the chat!");
                }
                else if (id == Packet.ServerPacket.LEAVE)
                {
                    string who = p.s_cont;

                    Log($"{who} had left the chat!");
                    TerminateSession(who);
                } else if (id == Packet.ServerPacket.TX_KILL)
                {
                    string uid = p.s_cont;

                    var r = reqs.Find(z => z.UID == uid);
                    if (r == null) { continue; }

                    var isSlave = r.slave == nickname;
                    TerminateSession(isSlave ? r.master : r.slave);
                } else if (id == Packet.ServerPacket.ERROR)
                {
                    Log($"Fatal error! {p.s_cont}", "ERROR");
                }
            }
        }

        public void UpdateOnline(string[] userlist)
        {
            listBox1.Invoke((Action)(() => {
                listBox1.Items.Clear();

                foreach (string user in userlist)
                {
                    listBox1.Items.Add(user);
                }
            }));
            
        }

        public void TerminateSession(string who)
        {
            var zl = new List<Request>();

            reqs.ForEach(z =>
            {
                string s = z.slave;
                string m = z.master;

                var isSlave = nickname == z.slave;

                if (m == who || s == who)
                {
                    Log($"PM session with {(isSlave ? m : s)} was terminated.");

                    zl.Add(z);

                    var c = windows.ContainsKey(z);
                    if (c)
                    {
                        Invoke((Action)(() =>
                        {
                            windows[z].Close();
                        }));
                    }
                }
            });

            reqs.RemoveAll(z => zl.Contains(z));
        }

        private void ChatWnd_Shown(object sender, EventArgs e)
        {
            this.Text = $"InfinityClient - Chat Window - {nickname}";
            Log("Chat window called externally");
            ins = this;
            Connect();
        }

        private void ChatWnd_FormClosed(object sender, FormClosedEventArgs e)
        {
            Environment.Exit(0);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string m = textBox2.Text;
            textBox2.Clear();
            string[] a = m.Split(' ').Length == 0 ? new string[1] {m} : m.Split(' ');
            if (m.IsCommand("accept"))
            {
                if (a.Length != 2)
                {
                    Log("Usage: /accept <nickname>"); return;
                }

                string master = a[1];
                var r = reqs.Find(z => z.master == master);
                if (r == null)
                {
                    Log($"No PM offers were found by {master}. Did you mean /offer {master}?"); return;
                }

                Protocol.SendPacket(ns, PBuild.U_TXAccept(r));
            } else if (m.IsCommand("offer"))
            {
                if (a.Length != 2)
                {
                    Log("Usage: /offer <nickname>"); return;
                }

                string slave = a[1];

                if (slave == nickname)
                {
                    Log($"You can't establish a private chat with yourself."); return;
                }

                var r = reqs.Find(z => z.master == nickname && z.slave == slave);
                if (r != null)
                {
                    Log($"You had already made a PM offer to {slave}!"); return;
                }

                r = new Request();
                r.master = nickname;
                r.slave = slave;
                reqs.Add(r);

                Log($"Sent a PM offer to {slave}!");

                Protocol.SendPacket(ns, PBuild.U_TXOffer(r));
            }
            else {
                Protocol.SendPacket(ns, PBuild.U_SendMessage(m));
            }
        }

        private void textBox2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                button1_Click(this, new EventArgs());

                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void listBox1_DoubleClick(object sender, EventArgs e)
        {
            if (listBox1.SelectedItem != null)
            {
                string slave = listBox1.SelectedItem.ToString();

                if (slave == nickname)
                {
                    Log($"You can't establish a private chat with yourself."); return;
                }
                
                var r = reqs.Find(z => z.master == nickname && z.slave == slave);
                if (r != null)
                {
                    Log($"You had already made a PM offer to {slave}!"); return;
                }

                r = new Request();
                r.master = nickname;
                r.slave = slave;
                reqs.Add(r);

                Log($"Sent a PM offer to {slave}!");

                Protocol.SendPacket(ns, PBuild.U_TXOffer(r));
            }
        }
    }
}
