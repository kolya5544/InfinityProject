using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Flawless2;
using InfinityProtocol;

namespace InfinityClient
{
    public partial class PMWnd : Form
    {
        public FlawlessAlgo fless;

        public PMWnd()
        {
            InitializeComponent();
        }

        public RNGCryptoServiceProvider RNG = new RNGCryptoServiceProvider();
        public Request r = null;
        public bool isSlave = false;

        public void Log(string msg, string sender = "SYSTEM")
        {
            textBox1.AppendText($"[{DateTimeOffset.Now:HH:mm:ss}] <{sender}> {msg}\r\n");
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string m = textBox2.Text;
            textBox2.Clear();

            var b = fless.Encrypt(Encoding.UTF8.GetBytes(m)).ToArray();

            Protocol.SendPacket(ChatWnd.ns, PBuild.U_TXSend(r, $"enc {Convert.ToBase64String(b)}"));
            Log(m, ChatWnd.nickname);
        }

        private void PMWnd_Shown(object sender, EventArgs e)
        {
            listBox1.Items.Add(r.master);
            listBox1.Items.Add(r.slave);

            Log($"Opening chat session {r.UID}...");

            int bytes = 512;

            r.privateKey = new byte[bytes];
            RNG.GetBytes(r.privateKey);

            if (r.slave == ChatWnd.nickname)
            {
                isSlave = true;
            }

            this.Text = $"InfinityClient - Private Messages Window - {ChatWnd.nickname}/{(isSlave ? r.master : r.slave)}";

            if (isSlave)
            {
                Log("You're RECEIVING side. Waiting for SENDING side to initiate DH...");
            } else
            {
                Log("You're SENDING side. Initiating DH...");

                r.publicKey1 = new byte[bytes];
                r.publicKey2 = new byte[bytes];

                RNG.GetBytes(r.publicKey1);
                RNG.GetBytes(r.publicKey2);

                BigInteger Public = BigInteger.Abs(new BigInteger(r.publicKey1));
                BigInteger Generator = BigInteger.Abs(new BigInteger(r.publicKey2));
                BigInteger Private = BigInteger.Abs(new BigInteger(r.privateKey));

                r.masterMix = BigInteger.ModPow(Generator, Private, Public).ToByteArray();

                Protocol.SendPacket(ChatWnd.ns, PBuild.U_TXSend(r, $"master {Convert.ToBase64String(r.publicKey1)} {Convert.ToBase64String(r.publicKey2)} {Convert.ToBase64String(r.masterMix)}"));
            }
        }

        public void GotMasterMix()
        {
            Log("Got sending mix and public generators. Now preparing and sending own mixture...");

            BigInteger Public = BigInteger.Abs(new BigInteger(r.publicKey1));
            BigInteger Generator = BigInteger.Abs(new BigInteger(r.publicKey2));
            BigInteger Private = BigInteger.Abs(new BigInteger(r.privateKey));

            r.slaveMix = BigInteger.ModPow(Generator, Private, Public).ToByteArray();

            Protocol.SendPacket(ChatWnd.ns, PBuild.U_TXSend(r, $"slave {Convert.ToBase64String(r.slaveMix)}"));

            Log("Calculating common key and hashing it...");

            BigInteger MRBI = BigInteger.Abs(new BigInteger(r.masterMix));

            string hash = "";
            using (SHA256 sha256 = SHA256.Create())
            {
                r.agreedKey = sha256.ComputeHash(BigInteger.ModPow(MRBI, Private, Public).ToByteArray());
                hash = BitConverter.ToString(sha256.ComputeHash(r.agreedKey)).Replace("-", "");
            }

            Log($"Hash result -> {hash}. CONNECTION ESTABLISHED SUCCESSFULLY!");

            Protocol.SendPacket(ChatWnd.ns, PBuild.U_TXSend(r, $"hash {hash}"));

            fless = new FlawlessAlgo();
            fless.InitialKey = BitConverter.ToString(r.agreedKey).Replace("-", "");
        }

        public void GotSlaveMix()
        {
            Log("Got receiving mix. Calculating common key and hashing it...");

            BigInteger Public = BigInteger.Abs(new BigInteger(r.publicKey1));
            BigInteger Private = BigInteger.Abs(new BigInteger(r.privateKey));
            BigInteger MRBI = BigInteger.Abs(new BigInteger(r.slaveMix));

            string hash = "";
            using (SHA256 sha256 = SHA256.Create())
            {
                r.agreedKey = sha256.ComputeHash(BigInteger.ModPow(MRBI, Private, Public).ToByteArray());
                hash = BitConverter.ToString(sha256.ComputeHash(r.agreedKey)).Replace("-", "");
            }

            Log($"Hash result -> {hash}. CONNECTION ESTABLISHED SUCCESSFULLY!");

            Protocol.SendPacket(ChatWnd.ns, PBuild.U_TXSend(r, $"hash {hash}"));

            fless = new FlawlessAlgo();
            fless.InitialKey = BitConverter.ToString(r.agreedKey).Replace("-", "");
        }

        public void GotHash(string hash)
        {
            Log($"Got hash from the other side. Seems to be {hash}. If it doesn't match, please close the connection IMMEDIATELY!");
        }

        public void GotMsg(string msg)
        {
            string[] c = msg.Split(' ');
            string cont = c[1];

            var m = fless.Decrypt(Convert.FromBase64String(cont)).ToArray();
            string sent = Encoding.UTF8.GetString(m);

            Log(sent, isSlave ? r.master : r.slave);
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

        private void PMWnd_FormClosed(object sender, FormClosedEventArgs e)
        {
            Protocol.SendPacket(ChatWnd.ns, PBuild.U_TXKill(r));
            //ChatWnd.ins.TerminateSession(isSlave ? r.master : r.slave);
        }
    }
}
