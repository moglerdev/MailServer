// MailServer - Easy and fast Mailserver
//
// Copyright(C) 2020 Christopher Mogler
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with this program.If not, see<https://www.gnu.org/licenses/>.

using MailServer.Common;
using MailServer.MTA;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

// TODO:
//  - Database 
//  - RFC 
//  - SMTP (MTA)
//  - IMAP (MDA)
//  - Authentication
//  - WebAPI (Configuration and MUA)

namespace MailServer {
    class Program {

        static void Main(string[] args)
        {
            Config.Current = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(File.ReadAllText("Config.json"));
            MailTransferAgent.Certificate = new X509Certificate2(Config.Current.Certificate.Filename, Config.Current.Certificate.Password, X509KeyStorageFlags.MachineKeySet);

            MailTransferAgent mta = new MailTransferAgent(25);

            try
            {
                Task mtaTask = mta.StartAsync();

                Log.WriteLine(LogType.Info, "Program", "Main", "MailTransferAgent-Service has been successfully started.");

                mtaTask.Wait();
            }
            catch(Exception e)
            {
                mta.Stop();
                Log.WriteLine(LogType.Info, "Program", "Main", "MailTransferAgent-Service failed to start!");
            }
        }
    }
}