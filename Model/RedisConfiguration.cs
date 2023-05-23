using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BenchamarkHelper.Model
{
    public class ServerDetail
    {
        public string User { get; set; }
        public string Host { get; set; }

        public string Server { get; set; }
    }

    public class RedisConfiguration
    {
        public List<string> DefaultLoad { get; set; }
        public string CustomLoad { get; set; }
        public List<string> Environment { get; set; }
        public ServerDetail Gating { get; set; }
        public ServerDetail Lab { get; set; }
        public string PrivateKeyPath { get; set; }
    }

}
