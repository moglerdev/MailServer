using System;
using System.Collections.Generic;
using System.Text;

namespace MailServer.Common {
    public class Config {
        public static Config Current;
        public String Listen { get; set; }
        public List<String> Accounts { get; set; }
    }
}
