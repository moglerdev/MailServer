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

using DnsClient;
using DnsClient.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MailServer.Common {
    public class DnsHelper {
        private static LookupClient _DnsClient = new LookupClient(); // TODO: DNS-IP einstellbar machen
        public static String ReverseLookUp(IPAddress address)
        {
            IDnsQueryResponse query  = _DnsClient.QueryReverse(address);
            return query.Answers.PtrRecords().FirstOrDefault()?.PtrDomainName;
        }

        public static List<IPAddress> GetIPAddresses(String domain)
        {
            List<IPAddress> result = new List<IPAddress>();
            var q = _DnsClient.Query(domain, QueryType.A);
            foreach (ARecord adr in q.AllRecords.ARecords())
                result.Add(adr.Address);
            q = _DnsClient.Query(domain, QueryType.AAAA);
            foreach (AaaaRecord adr in q.AllRecords.AaaaRecords())
                result.Add(adr.Address);
            return result;
        }

        public static List<IPAddress> GetMxRecords(String domain)
        {
            List<IPAddress> result = new List<IPAddress>();
            var q = _DnsClient.Query(domain, QueryType.MX);
            foreach(MxRecord mx in q.AllRecords.MxRecords())
            {
                result.AddRange(GetIPAddresses(mx.Exchange.Value));
            }

            return result;
        }

        public static Boolean CheckSpf(String mailAddress, IPAddress address)
        {
            Regex regexMailDomain = new Regex("[@].{1,}", RegexOptions.Compiled);
            String emailDomain = regexMailDomain.Match(mailAddress)?.Value.Substring(1);

            if (emailDomain == null)
                return false;

            SpfRecord spf = GetSpfRecord(emailDomain);

            if (spf == null) // TODO: Spamschutz
                return true;

            return !spf.GetSpfRecord(address).HasFlag(SpfRecordTypes.Fail);
        }

        public static SpfRecord GetSpfRecord(String domain)
        {
            IEnumerable<TxtRecord> records = _DnsClient.Query(domain, QueryType.TXT)?.Answers.TxtRecords();
            foreach (TxtRecord rec in records)
                foreach (String val in rec.Text)
                {
                    if (val.Length > 5 && val.Trim().Substring(0, 5) == "v=spf")
                        return new SpfRecord(domain, val);
                }
            return null;
        }
    }
}
