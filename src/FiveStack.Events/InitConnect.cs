using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private static int PasswordBufferLength = 86;
    public static nint PasswordBuffer { get; set; } = nint.Zero;
    public static Dictionary<ulong, string> PendingPlayers = new();

    // near "CNetworkGameServerBase::ConnectClient( name=\'%s\', remote=\'%s\' )\n"
    private static string ConnectClientSignature = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        ? "55 48 89 E5 41 57 41 56 41 89 CE 41 55 41 54 4D 89 CC 53 48 89 D3 48 81 EC F8 03 00 00 8B 45 20 48 89 BD 48 FC FF FF 48 8B 3D ?? ?? ?? ?? 48 89 B5 40 FC FF FF 48 C7 85 60 FC FF FF 00 00 00 00 89 8D 30 FC FF FF 89 85 10 FC FF FF 48 8B 07 4C 89 85 38 FC FF FF FF 90 60 01 00 00 44 89 F6 48 89 C7 48 8D 85 10 FD FF FF 48 89 C2 48 89 85 28 FC FF FF 48 8B 07 FF 50 78 84 C0 0F 84 ?? ?? ?? ??"
        : "";
    /// <summary>
    /// <c>
    /// virtual CServerSideClientBase* CNetworkGameServerBase::ConnectClient(
    /// 	const char* name,
    /// 	ns_address* address,
    /// 	void* netInfo,
    /// 	C2S_CONNECT_Message* connectMsg,
    /// 	const char* password,
    /// 	const byte* authTicket,
    /// 	int authTicketLength,
    /// 	bool isLowViolence);
    /// </c>
    /// </summary>
    public static MemoryFunctionWithReturn<
        nint,
        nint,
        nint,
        nint,
        nint,
        nint,
        nint,
        int,
        bool,
        nint
    > ConnectClientFunc = new(ConnectClientSignature, Addresses.EnginePath);
    public static Func<nint, nint, nint, nint, nint, nint, nint, int, bool, nint> ConnectClient =
        ConnectClientFunc.Invoke;

    private HookResult ConnectClientHook(DynamicHook hook)
    {
        var authTicket = hook.GetParamArray<byte>(6, 7);
        var token = hook.GetParam<string>(5);
        var steamId = MemoryMarshal.Read<ulong>(authTicket[..8]);

        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return HookResult.Continue;
        }

        var matchPassword = match.password;

        if (token == null)
        {
            hook.SetParam(6, 0);
            hook.SetParam(7, 0);
            return HookResult.Continue;
        }

        if (token == matchPassword)
        {
            return HookResult.Continue;
        }

        MatchMember? member = MatchUtility.GetMemberFromLineup(match, steamId.ToString(), token);

        if (member != null)
        {
            hook.SetParam(5, PasswordBuffer);
            return HookResult.Continue;
        }

        var matchId = match.id;

        string[] parts = token.Split(':');

        if (parts.Length != 3)
        {
            hook.SetParam(6, 0);
            hook.SetParam(7, 0);
            return HookResult.Continue;
        }

        string type = parts[0];
        string role = parts[1];
        string password = parts.Length > 1 ? parts[2] : "";

        var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(matchPassword));
        var computedHash = hmac.ComputeHash(
            Encoding.UTF8.GetBytes($"{type}:{role}:{steamId}:{matchId}")
        );
        var computedToken = Convert.ToBase64String(computedHash);

        // fix + and - for URL safe characters
        password = password.Replace("-", "+");
        password = password.Replace("_", "/");

        if (computedToken != password)
        {
            if (type == "tv")
            {
                hook.SetParam(6, 0);
                hook.SetParam(7, 0);
            }
            return HookResult.Continue;
        }

        ePlayerRoles playerRole = PlayerRoleUtility.PlayerRoleStringToEnum(role);

        if (
            type == "game"
            && (
                playerRole == ePlayerRoles.Administrator
                || playerRole == ePlayerRoles.TournamentOrganizer
                || playerRole == ePlayerRoles.MatchOrganizer
            )
        )
        {
            PendingPlayers[steamId] =
                playerRole == ePlayerRoles.Administrator ? "admin" : "organizer";
        }

        hook.SetParam(5, PasswordBuffer);

        return HookResult.Continue;
    }

    public static void SetPasswordBuffer(string password)
    {
        PasswordBuffer = Marshal.StringToCoTaskMemUTF8(new string('\0', PasswordBufferLength));
        StrCpy(PasswordBuffer, password);
    }

    private static unsafe void StrCpy(nint dst, string src)
    {
        Span<byte> buffer = stackalloc byte[PasswordBufferLength];

        int length = Encoding.UTF8.GetBytes(src, buffer[..(buffer.Length - 1)]);
        buffer[length] = (byte)'\0';

        var dstBuffer = new Span<byte>((byte*)dst, PasswordBufferLength);
        buffer.CopyTo(dstBuffer);
    }
}

public static class DynamicHookExtensions
{
    public static unsafe Span<T> GetParamArray<T>(
        this DynamicHook hook,
        int paramIndex,
        int lengthParamIndex
    )
    {
        var value = hook.GetParam<nint>(paramIndex);
        var length = hook.GetParam<int>(lengthParamIndex);
        return new Span<T>((void*)value, length);
    }
}
