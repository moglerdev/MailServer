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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MailServer.Common {
    public static class IPAddressExtensions {

        public static bool IsInSubnet(this IPAddress address, string subnetMask)
        {
            Int32 slashIdx = subnetMask.IndexOf("/");
            if (slashIdx == -1)
                throw new NotSupportedException("Only SubNetMasks with a given prefix length are supported.");

            IPAddress maskAddress = IPAddress.Parse(subnetMask.Substring(0, slashIdx));

            if (maskAddress.AddressFamily != address.AddressFamily)
                return false;

            Int32 maskLength = Int32.Parse(subnetMask.Substring(slashIdx + 1));

            if (maskAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                UInt32 maskAddressBits = BitConverter.ToUInt32(maskAddress.GetAddressBytes().Reverse().ToArray(), 0);
                UInt32 ipAddressBits = BitConverter.ToUInt32(address.GetAddressBytes().Reverse().ToArray(), 0);
                UInt32 mask = uint.MaxValue << (32 - maskLength);
                return ( maskAddressBits & mask ) == ( ipAddressBits & mask );
            }

            if (maskAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                BitArray maskAddressBits = new BitArray(maskAddress.GetAddressBytes());
                BitArray ipAddressBits = new BitArray(address.GetAddressBytes());
                if (maskAddressBits.Length != ipAddressBits.Length)
                    throw new ArgumentException("Length of IP Address and Subnet Mask do not match.");
                for (Int32 i = 0; i < maskLength; i++)
                    if (ipAddressBits[i] != maskAddressBits[i])
                        return false;

                return true;
            }
            throw new NotSupportedException("Only InterNetworkV6 or InterNetwork address families are supported.");
        }
    }
}
