using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InfinityClient
{
    public partial class Form1 : Form
    {
        public static Random rng = new Random();

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            letterCompression.Letter.Init();

            textBox2.Text = $"guest{rng.Next(100,999)}";
        }

        private void button1_Click(object sender, EventArgs e)
        {
            var cwnd = new ChatWnd();
            ChatWnd.hostname = textBox1.Text;
            ChatWnd.nickname = textBox2.Text;
            cwnd.Show();
            Hide();
        }
    }

    public static class Ext
    {
        public static bool IsCommand(this string c, string cmd)
        {
            if (c.ToLower().StartsWith($"/{cmd}"))
            {
                return true;
            }
            return false;
        }
    }
}
