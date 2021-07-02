using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace HyPlayer.Classes
{
    public static class UAHelper
    {
        const int URLMON_OPTION_USERAGENT = 0x10000001;

        [DllImport("urlmon.dll", CharSet = CharSet.Ansi)]
        private static extern int UrlMkSetSessionOption(int dwOption, string pBuffer, int dwBufferLength, int dwReserved);

        [DllImport("urlmon.dll", CharSet = CharSet.Ansi)]
        private static extern int UrlMkGetSessionOption(int dwOption, StringBuilder pBuffer, int dwBufferLength, ref int pdwBufferLength, int dwReserved);

        public static string GetUserAgent()
        {
            int capacity = 255;
            var buf = new StringBuilder(capacity);
            int length = 0;

            UrlMkGetSessionOption(URLMON_OPTION_USERAGENT, buf, capacity, ref length, 0);

            return buf.ToString();
        }

        public static void SetUserAgent(string agent)
        {
            var hr = UrlMkSetSessionOption(URLMON_OPTION_USERAGENT, agent, agent.Length, 0);
            var ex = Marshal.GetExceptionForHR(hr);
            if (null != ex)
            {
                throw ex;
            }
        }

        public static void AppendUserAgent(string suffix)
        {
            SetUserAgent(GetUserAgent() + suffix);
        }
    }
}
