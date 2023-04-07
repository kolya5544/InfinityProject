using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfinityServer
{
    public class Config
    {
        public string hostname = "127.0.0.1";
        public int port = 7670;

        public string serverName = "IKTeam Server";
        public string MOTD = "Hello! This is anonymous server with no rules!\nHave fun!";

        public static Config LoadConfig(string filepath)
        {
            if (File.Exists(filepath))
            {
                return JsonConvert.DeserializeObject<Config>(File.ReadAllText(filepath));
            }
            else
            {
                Config cfg = new Config();
                cfg.SaveConfig(filepath);
                return cfg;
            }
        }

        public void SaveConfig(string filepath)
        {
            File.WriteAllText(filepath, JsonConvert.SerializeObject(this));
        }
    }
}
