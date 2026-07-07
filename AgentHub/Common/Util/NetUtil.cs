using System.Net;

namespace AgentHub.Common.Util
{
    /// <summary>네트워크 관련 유틸.</summary>
    public static class NetUtil
    {
        /// <summary>
        /// 요청 원격 주소가 loopback(PC 본체)인지 판별한다.
        /// EmbedIO 듀얼스택 리스너는 IPv4 loopback(127.0.0.1) 연결을 IPv4-mapped IPv6(::ffff:127.0.0.1)로
        /// 보고하는데, .NET Framework의 <see cref="IPAddress.IsLoopback"/>은 이를 loopback으로 인식하지 못한다.
        /// 따라서 IPv4-mapped 주소는 먼저 IPv4로 변환한 뒤 판별한다.
        /// </summary>
        public static bool IsLoopback(IPAddress address)
        {
            if (address == null) return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return IPAddress.IsLoopback(address);
        }
    }
}
