using System;
using System.Net;
using System.Text;

namespace BattleshipsVR.Session
{
    /// <summary>Encodes IPv4 and port into a short base32 code for verbal sharing</summary>
    public static class SessionCodeCodec
    {
        private const string ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // avoids I and O for readability

        /// <summary>Returns compact code for ip and port</summary>
        public static string Encode(string ipv4, ushort port)
        {
            byte[] ip = IPAddress.Parse(ipv4).GetAddressBytes();
            if (ip.Length != 4) throw new ArgumentException("IPv6 not supported for code");

            byte[] data = new byte[6];
            Buffer.BlockCopy(ip, 0, data, 0, 4);
            data[4] = (byte)(port >> 8);
            data[5] = (byte)(port & 0xFF);

            return ToBase32(data);
        }

        /// <summary>Parses code back into ip and port</summary>
        public static bool TryDecode(string code, out string ipv4, out ushort port)
        {
            ipv4 = BattleshipsVR.Bootstrap.NetConstants.DEFAULT_LOCAL_IP;
            port = BattleshipsVR.Bootstrap.NetConstants.DEFAULT_PORT;

            if (!FromBase32(code, out byte[] data) || data == null || data.Length != 6)
                return false;

            ipv4 = new IPAddress(new byte[] { data[0], data[1], data[2], data[3] }).ToString();
            port = (ushort)((data[4] << 8) | data[5]);
            return true;
        }

        private static string ToBase32(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder();
            int bits = 0;
            int value = 0;

            foreach (byte b in bytes)
            {
                value = (value << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    int idx = (value >> (bits - 5)) & 31;
                    sb.Append(ALPHABET[idx]);
                    bits -= 5;
                }
            }

            if (bits > 0)
            {
                int idx = (value << (5 - bits)) & 31;
                sb.Append(ALPHABET[idx]);
            }

            return sb.ToString();
        }

        private static bool FromBase32(string code, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrWhiteSpace(code))
                return false;

            int bits = 0;
            int value = 0;
            System.Collections.Generic.List<byte> list = new();

            foreach (char ch in code.Trim().ToUpperInvariant())
            {
                int idx = ALPHABET.IndexOf(ch);
                if (idx < 0) return false;

                value = (value << 5) | idx;
                bits += 5;

                if (bits >= 8)
                {
                    list.Add((byte)((value >> (bits - 8)) & 0xFF));
                    bits -= 8;
                }
            }

            bytes = list.ToArray();
            return true;
        }
    }
}
