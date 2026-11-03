namespace FiveStack.Utilities;

public sealed class PracticeAfk
{
    // Below these, a change between two one-second samples is aim jitter or a
    // pawn settling on a slope, not a person.
    public const float LookDegrees = 1f;
    public const float MoveUnits = 4f;

    public const int MaxKickMinutes = 1440;

    public const int MaxWarnLeadSeconds = 300;
    public const int MaxFinalLeadSeconds = 60;

    public enum Step
    {
        None,
        Warn,
        FinalWarn,
        Kick,
    }

    public readonly record struct Sample(
        ulong SteamId,
        bool InGame,
        bool Alive,
        bool Stirred,
        ulong Buttons,
        (float Yaw, float Pitch)? View,
        (float X, float Y, float Z)? At,
        bool Roaming = false
    );

    public readonly record struct Due(ulong SteamId, Step Step, int SecondsLeft, int IdleSeconds);

    public static void Dispatch(IEnumerable<Due> due, Action<Due> act, Action<Due, Exception> failed)
    {
        foreach (Due step in due)
        {
            try
            {
                act(step);
            }
            catch (Exception error)
            {
                failed(step, error);
            }
        }
    }

    public static int ClampMinutes(double? minutes)
    {
        if (minutes is not { } value || double.IsNaN(value) || value <= 0)
        {
            return 0;
        }

        return value >= MaxKickMinutes ? MaxKickMinutes : (int)Math.Floor(value);
    }

    private sealed class Clock
    {
        public int LastActive;
        public Step Shown;
        public int KickAt;
        public (float Yaw, float Pitch)? View;
        public (float X, float Y, float Z)? At;
    }

    private readonly Dictionary<ulong, Clock> _clocks = new();

    // A player seen for the first time starts a clock rather than being judged
    // on time nobody watched.
    public IReadOnlyList<Due> Sweep(IEnumerable<Sample> players, int now, int kickMinutes)
    {
        var due = new List<Due>();

        kickMinutes = ClampMinutes(kickMinutes);

        if (kickMinutes == 0)
        {
            _clocks.Clear();

            return due;
        }

        int kick = kickMinutes * 60;
        (int warnLead, int finalLead) = Leads(kick);
        var present = new HashSet<ulong>();

        foreach (Sample player in players)
        {
            present.Add(player.SteamId);

            // A spectator in eye or chase sees through whoever they follow, so
            // only free roam is their own view and seat.
            bool ownView = player.Alive || player.Roaming;
            (float Yaw, float Pitch)? view = ownView ? player.View : null;
            (float X, float Y, float Z)? at = ownView ? player.At : null;

            if (!_clocks.TryGetValue(player.SteamId, out Clock? clock))
            {
                _clocks[player.SteamId] = new Clock
                {
                    LastActive = now,
                    View = view,
                    At = at,
                };

                continue;
            }

            bool stirred =
                !player.InGame
                || player.Stirred
                || (player.Alive && player.Buttons != 0)
                || Looked(clock.View, view)
                || Moved(clock.At, at);

            clock.View = view;
            clock.At = at;

            if (stirred)
            {
                clock.LastActive = now;
                clock.Shown = Step.None;
                clock.KickAt = 0;

                continue;
            }

            if (
                clock.Shown is Step.Warn or Step.FinalWarn
                && clock.LastActive + kick > clock.KickAt
            )
            {
                clock.Shown = Step.None;
                clock.KickAt = 0;
            }

            Step step = Next(clock, now, kick, warnLead, finalLead);
            int idle = now - clock.LastActive;

            if (step == Step.Warn)
            {
                due.Add(new Due(player.SteamId, step, warnLead, idle));
            }
            else if (step == Step.FinalWarn)
            {
                due.Add(new Due(player.SteamId, step, finalLead, idle));
            }
            else if (step == Step.Kick)
            {
                due.Add(new Due(player.SteamId, step, 0, idle));
            }
        }

        foreach (ulong gone in _clocks.Keys.Where(steamId => !present.Contains(steamId)).ToList())
        {
            _clocks.Remove(gone);
        }

        return due;
    }

    // The kick time is fixed when the first warning is given, so a warning
    // always means what it said: a shorter limit set afterwards never brings
    // the kick forward.
    private static Step Next(Clock clock, int now, int kick, int warnLead, int finalLead)
    {
        if (clock.Shown == Step.None && now - clock.LastActive >= kick - warnLead)
        {
            clock.Shown = Step.Warn;
            clock.KickAt = now + warnLead;

            return Step.Warn;
        }

        if (clock.Shown == Step.Warn && now >= clock.KickAt - finalLead)
        {
            clock.Shown = Step.FinalWarn;

            return Step.FinalWarn;
        }

        if (clock.Shown == Step.FinalWarn && now >= clock.KickAt)
        {
            clock.Shown = Step.Kick;

            return Step.Kick;
        }

        return Step.None;
    }

    public void Forget(ulong steamId)
    {
        _clocks.Remove(steamId);
    }

    public void Reset()
    {
        _clocks.Clear();
    }

    // Whole minutes once there are any, so a warning reads "2 minutes" rather
    // than "150 seconds".
    public static (int Warn, int Final) Leads(int kickSeconds)
    {
        int warn = Math.Min(MaxWarnLeadSeconds, kickSeconds / 2);

        if (warn >= 60)
        {
            warn -= warn % 60;
        }

        return (warn, Math.Min(MaxFinalLeadSeconds, warn / 2));
    }

    public static bool Looked((float Yaw, float Pitch)? before, (float Yaw, float Pitch)? now)
    {
        if (before is not { } was || now is not { } @is)
        {
            return false;
        }

        float yaw = MathF.Abs(((@is.Yaw - was.Yaw) % 360f + 540f) % 360f - 180f);
        float pitch = MathF.Abs(@is.Pitch - was.Pitch);

        return yaw >= LookDegrees || pitch >= LookDegrees;
    }

    public static bool Moved((float X, float Y, float Z)? before, (float X, float Y, float Z)? now)
    {
        if (before is not { } was || now is not { } @is)
        {
            return false;
        }

        float dx = @is.X - was.X;
        float dy = @is.Y - was.Y;
        float dz = @is.Z - was.Z;

        return dx * dx + dy * dy + dz * dz >= MoveUnits * MoveUnits;
    }

    public static string Warning(Step step, int secondsLeft)
    {
        if (step == Step.FinalWarn)
        {
            return $"Final warning: you will be kicked for being idle in {Duration(secondsLeft)}.";
        }

        return $"Are you still there? Move or press any key, or you will be kicked for being idle in {Duration(secondsLeft)}.";
    }

    public static string KickReason(int idleSeconds)
    {
        int shown = idleSeconds >= 120 ? idleSeconds - idleSeconds % 60 : idleSeconds;

        return $"Kicked for being idle for {Duration(shown)} on a practice server";
    }

    private static string Duration(int seconds)
    {
        if (seconds >= 60 && seconds % 60 == 0)
        {
            int minutes = seconds / 60;

            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }

        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }
}
