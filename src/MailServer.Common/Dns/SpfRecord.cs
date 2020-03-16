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
// You should have received a copy of the GNU General Public License along with this program. If not, see<https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

// TODO: RecursiveLoop verhindern

namespace MailServer.Common {
    public enum SpfRecordTypes {
        /// <summary>
        /// + 
        /// </summary>
        Pass,
        /// <summary>
        /// -
        /// </summary>
        Fail,
        /// <summary>
        /// ~
        /// </summary>
        SoftFail,
        /// <summary>
        /// ?
        /// </summary>
        Neutral
    }

    public class SpfRecord {
        public String Version { get; set; }
        public String Domain { get; set; }
        public SpfRecordTypes All { get; set; }
        public Dictionary<IPAddress, SpfRecordTypes> IPs { get; set; } = new Dictionary<IPAddress, SpfRecordTypes>();
        public Dictionary<String, SpfRecordTypes> SubnetMasks { get; set; } = new Dictionary<string, SpfRecordTypes>();
        public Dictionary<String, SpfRecordTypes> Includes { get; set; } = new Dictionary<String, SpfRecordTypes>();
        public Dictionary<String, SpfRecordTypes> MX_Records { get; set; } = new Dictionary<String, SpfRecordTypes>();
        public Dictionary<String, SpfRecordTypes> A_Records { get; set; } = new Dictionary<String, SpfRecordTypes>();

        public SpfRecord(String domain) // v=spf1 ip4:213.165.64.0/23 -all
        {
            this.Domain = domain;
        }

        public SpfRecord(String domain, String spf)
        {
            this.Domain = domain;
            this.FillSpf(spf);
        }

        public void FillSpf(String spf)
        {
            File.AppendAllText("test.txt", spf + "\r\n", Encoding.UTF8);
            String[] items = spf.Split(' ');
            foreach (String item in items)
            {
                try
                {
                    SpfRecordTypes type = SpfRecordTypes.Pass;

                    String[] val = item.Trim().Split(':', 2);
                    Boolean hasPrefix = true;

                    switch (val[0][0])
                    {
                        case '-': type = SpfRecordTypes.Fail; break;
                        case '~': type = SpfRecordTypes.SoftFail; break;
                        case '?': type = SpfRecordTypes.Neutral; break;
                        case '+': type = SpfRecordTypes.Pass; break;
                        case 'v': this.Version = val[0].Substring(2); continue;
                        default:
                            type = SpfRecordTypes.Pass;
                            hasPrefix = false;
                            break;
                    }

                    if (hasPrefix)
                        val[0] = val[0].Substring(1);
                    // TODO: Redirect
                    switch (val[0].ToLower())
                    {
                        case "ip4":
                            if (val[1].IndexOf('/') < 0)
                                IPs.Add(IPAddress.Parse(val[1]), type);
                            else
                                SubnetMasks.Add(val[1], type);
                            break;
                        case "ip6": 
                            if(val[1].IndexOf('/') < 0)
                                IPs.Add(IPAddress.Parse(val[1]), type);
                            else
                                SubnetMasks.Add(val[1], type); 
                            break;
                        case "mx":
                            if (val.Length < 2)
                                MX_Records.Add(this.Domain, type);
                            else
                                MX_Records.Add(val[1], type);
                            break;
                        case "a":
                            if (val.Length < 2)
                                A_Records.Add(this.Domain, type);
                            else
                                A_Records.Add(val[1], type);
                            break;
                        case "include": Includes.Add(val[1], type); break;
                        case "all": All = type; break;
                        default: break;
                    }
                }
                catch(Exception e)
                {
                    File.AppendAllText("test.txt", e.ToString() + "\r\n", Encoding.UTF8);
                }
            }
        }

        public SpfRecordTypes GetSpfRecord(IPAddress address)
        {
            var spfsDomain = new List<string>();
            return GetSpfRecord(address, this, ref spfsDomain) ?? this.All;
        }

        private SpfRecordTypes? GetSpfRecord(IPAddress address, SpfRecord rec, ref List<String> spfs)
        {
            if (spfs.Count > 10)
                return null;

            spfs.Add(rec.Domain);

            foreach (var spfAdr in rec.IPs)
                if (spfAdr.Key.Equals(address))
                    return spfAdr.Value;

            foreach (var sub in rec.SubnetMasks)
                if (address.IsInSubnet(sub.Key))
                    return sub.Value;

            foreach (var mx in rec.MX_Records)
            {
                var dns = DnsHelper.GetMxRecords(mx.Key);
                foreach (IPAddress dnsAdr in dns)
                    if (dnsAdr.Equals(address))
                        return mx.Value;
            }

            foreach(var a in rec.A_Records)
            {
                var dns = DnsHelper.GetIPAddresses(a.Key);
                foreach (IPAddress aAdr in dns)
                    if (aAdr.Equals(address))
                        return a.Value;
            }

            foreach(SpfRecord spfRec in rec.GetIncludes())
            {
                if (spfs.Any(x => x == spfRec.Domain))
                    continue;
                SpfRecordTypes? res = this.GetSpfRecord(address, spfRec, ref spfs);
                spfs.Remove(spfRec.Domain);
                if (res == null)
                    continue;
                return res;
            }

            return null;
        }

        public List<SpfRecord> GetIncludes()
        {
            List<SpfRecord> record = new List<SpfRecord>();
            foreach(var incl in this.Includes)
                record.Add(DnsHelper.GetSpfRecord(incl.Key));

            return record;
        }
    }
}
