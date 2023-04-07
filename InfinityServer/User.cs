using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace InfinityServer
{
    public class User
    {
        public bool Authed = false;

        public string Nickname;

        public NetworkStream ns;
    }
}
