using System.Runtime.InteropServices;
using System.Text;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Memory;

namespace FiveStack;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate nint ConnectClientDelegate(
    nint param1,
    nint param2,
    nint param3,
    nint param4,
    nint param5,
    nint param6,
    nint param7,
    int param8,
    bool param9
);

public partial class FiveStackPlugin
{
    private static int PasswordBufferLength = 86;
    public static nint PasswordBuffer { get; set; } = nint.Zero;
    public static Dictionary<ulong, string> PendingPlayers = new();

    private static string ConnectClientSignature = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        ? "55 48 89 E5 41 57 41 56 41 89 CE 41 55 41 54 4D 89 CC 53 48 89 D3 48 81 EC ? ? ? ? 8B 45 20"
        : "4C 8B CE 8B D3 ? ? ? ?";

    private IUnmanagedFunction<ConnectClientDelegate>? _connectClientFunc;
    private Guid _connectClientHookId;

    private void InitializeConnectClientHook()
    {
        try
        {
            if (_connectClientFunc != null)
            {
                return;
            }

            if (string.IsNullOrEmpty(ConnectClientSignature))
            {
                _logger.LogWarning("ConnectClient signature is not available for this platform");
                return;
            }

            var address = Core.Memory.GetAddressBySignature(Library.Engine, ConnectClientSignature);

            if (address == null || address == nint.Zero)
            {
                _logger.LogWarning("Failed to find ConnectClient signature");
                return;
            }

            _connectClientFunc = Core.Memory.GetUnmanagedFunctionByAddress<ConnectClientDelegate>(
                address.Value
            );

            if (_connectClientFunc == null)
            {
                _logger.LogWarning("Failed to get unmanaged function for ConnectClient");
                return;
            }

            _connectClientHookId = _connectClientFunc.AddHook(
                (next) =>
                {
                    return (
                        nint param1,
                        nint param2,
                        nint param3,
                        nint param4,
                        nint param5,
                        nint param6,
                        nint param7,
                        int param8,
                        bool param9
                    ) =>
                    {
                        var name = Marshal.PtrToStringUTF8(param2) ?? "";
                        var token = Marshal.PtrToStringUTF8(param6);

                        ulong steamId = 0;
                        unsafe
                        {
                            if (param7 != nint.Zero && param8 >= 8)
                            {
                                var authTicket = new Span<byte>((byte*)param7, param8);
                                steamId = MemoryMarshal.Read<ulong>(authTicket[..8]);
                            }
                        }

                        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

                        if (match == null)
                        {
                            return next()(
                                param1,
                                param2,
                                param3,
                                param4,
                                param5,
                                param6,
                                param7,
                                param8,
                                param9
                            );
                        }

                        var matchPassword = match.password;

                        if (token == null)
                        {
                            return next()(
                                param1,
                                param2,
                                param3,
                                param4,
                                param5,
                                param6,
                                nint.Zero,
                                0,
                                param9
                            );
                        }

                        nint effectivePassword =
                            PasswordBuffer != nint.Zero ? PasswordBuffer : param6;

                        if (token == matchPassword)
                        {
                            if (MatchUtility.HasPlaceholderMembers(match))
                            {
                                return next()(
                                    param1,
                                    param2,
                                    param3,
                                    param4,
                                    param5,
                                    effectivePassword,
                                    param7,
                                    param8,
                                    param9
                                );
                            }

                            PendingPlayers[steamId] = "streamer";
                            return next()(
                                param1,
                                param2,
                                param3,
                                param4,
                                param5,
                                param6,
                                param7,
                                param8,
                                param9
                            );
                        }

                        MatchMember? member = MatchUtility.GetMemberFromLineup(
                            match,
                            steamId.ToString(),
                            name
                        );

                        if (member != null)
                        {
                            return next()(
                                param1,
                                param2,
                                param3,
                                param4,
                                param5,
                                effectivePassword,
                                param7,
                                param8,
                                param9
                            );
                        }

                        var matchId = match.id;

                        string[] parts = token.Split(':');

                        if (parts.Length != 3)
                        {
                            return next()(
                                param1,
                                param2,
                                param3,
                                param4,
                                param5,
                                param6,
                                nint.Zero,
                                0,
                                param9
                            );
                        }

                        string type = parts[0];
                        string role = parts[1];
                        string password = parts[2];

                        var computedToken = ConnectAuth.ComputeExpectedToken(
                            matchPassword,
                            type,
                            role,
                            steamId,
                            matchId
                        );

                        password = ConnectAuth.NormalizeClientToken(password);

                        if (computedToken != password)
                        {
                            if (type == "tv")
                            {
                                return next()(
                                    param1,
                                    param2,
                                    param3,
                                    param4,
                                    param5,
                                    param6,
                                    nint.Zero,
                                    0,
                                    param9
                                );
                            }

                            return next()(
                                param1,
                                param2,
                                param3,
                                param4,
                                param5,
                                param6,
                                param7,
                                param8,
                                param9
                            );
                        }

                        ePlayerRoles playerRole = PlayerRoleUtility.PlayerRoleStringToEnum(role);

                        if (
                            type == "game"
                            && (
                                playerRole == ePlayerRoles.Administrator
                                || playerRole == ePlayerRoles.TournamentOrganizer
                                || playerRole == ePlayerRoles.MatchOrganizer
                                || playerRole == ePlayerRoles.Streamer
                            )
                        )
                        {
                            if (playerRole == ePlayerRoles.Administrator)
                            {
                                PendingPlayers[steamId] = "admin";
                            }
                            else if (playerRole == ePlayerRoles.Streamer)
                            {
                                PendingPlayers[steamId] = "streamer";
                            }
                            else
                            {
                                PendingPlayers[steamId] = "organizer";
                            }
                        }

                        return next()(
                            param1,
                            param2,
                            param3,
                            param4,
                            param5,
                            effectivePassword,
                            param7,
                            param8,
                            param9
                        );
                    };
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize ConnectClient hook");
        }
    }

    private void UninstallConnectClientHook()
    {
        try
        {
            if (_connectClientFunc != null && _connectClientHookId != Guid.Empty)
            {
                _connectClientFunc.RemoveHook(_connectClientHookId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove ConnectClient hook");
        }

        _connectClientFunc = null;
        _connectClientHookId = Guid.Empty;

        if (PasswordBuffer != nint.Zero)
        {
            Marshal.FreeCoTaskMem(PasswordBuffer);
            PasswordBuffer = nint.Zero;
        }
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
