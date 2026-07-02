using System.Security.Cryptography;
using System.Text;

namespace FiveStack.Utilities
{
    public static class ConnectAuth
    {
        public static string ComputeExpectedToken(
            string matchPassword,
            string type,
            string role,
            ulong steamId,
            Guid matchId
        )
        {
            using HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(matchPassword));
            byte[] hash = hmac.ComputeHash(
                Encoding.UTF8.GetBytes($"{type}:{role}:{steamId}:{matchId}")
            );
            return Convert.ToBase64String(hash);
        }

        public static string NormalizeClientToken(string token)
        {
            return token.Replace("-", "+").Replace("_", "/");
        }
    }
}
