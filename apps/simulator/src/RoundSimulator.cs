using System.Globalization;
using FiveStack.Enums;

namespace FiveStack.Simulator;

// At is seconds after the freeze time ended.
public sealed record PlannedEvent(double At, string Name, Dictionary<string, object> Data);

public sealed class RoundSetup
{
    public required int Round { get; init; }
    public required Guid MatchMapId { get; init; }
    public required string MapName { get; init; }
    public required List<SimPlayer> Terrorists { get; init; }
    public required List<SimPlayer> CounterTerrorists { get; init; }
    public double RoundTime { get; init; } = 115;
    public double BombTimer { get; init; } = 40;
}

public sealed class RoundOutcome
{
    public required Side Winner { get; init; }
    public required eWinReason Reason { get; init; }
    public required double Duration { get; init; }
    public required List<PlannedEvent> Events { get; init; }
    public required bool BombPlanted { get; init; }
    public required Dictionary<string, int> KillRewards { get; init; }
}

// Plays one round between whoever is on each side: utility, duels, the bomb,
// and the events the plugin would have sent for them.
public sealed class RoundSimulator
{
    private static readonly string[] BodyHitgroups =
    [
        "Chest",
        "Chest",
        "Stomach",
        "Stomach",
        "Left Arm",
        "Right Arm",
        "Left Leg",
        "Right Leg",
    ];

    private readonly RoundSetup _setup;
    private readonly Random _random;
    private readonly List<PlannedEvent> _events = new();
    private readonly Dictionary<string, Side> _sides = new();
    private readonly Dictionary<string, Dictionary<string, int>> _damageTaken = new();
    private readonly Dictionary<string, (SimPlayer By, double Until)> _blinded = new();
    private readonly Dictionary<string, int> _killRewards = new();

    private bool _planted;
    private double _explodeAt = double.PositiveInfinity;
    private double? _defuseAt;
    private SimPlayer? _planter;

    private RoundSimulator(RoundSetup setup, Random random)
    {
        _setup = setup;
        _random = random;

        foreach (SimPlayer player in setup.Terrorists)
        {
            _sides[player.SteamId] = Side.T;
        }

        foreach (SimPlayer player in setup.CounterTerrorists)
        {
            _sides[player.SteamId] = Side.CT;
        }
    }

    public static RoundOutcome Play(RoundSetup setup, Random random)
    {
        return new RoundSimulator(setup, random).Run();
    }

    private List<SimPlayer> Alive(Side side)
    {
        return (side == Side.T ? _setup.Terrorists : _setup.CounterTerrorists)
            .Where(player => player.Alive)
            .ToList();
    }

    private RoundOutcome Run()
    {
        var throws = new List<(double At, SimPlayer Thrower, string Type)>();
        foreach (SimPlayer player in _setup.Terrorists.Concat(_setup.CounterTerrorists))
        {
            foreach (string grenade in player.Loadout.Grenades)
            {
                throws.Add((2 + _random.NextDouble() * 38, player, grenade));
            }
        }
        throws.Sort((a, b) => a.At.CompareTo(b.At));

        int nextThrow = 0;
        // some rounds are fast executes, some are slow defaults that run out
        double tempo = 0.6 + _random.NextDouble() * 1.3;
        double nextFight = (6 + _random.NextDouble() * 10) * tempo;
        double? plantAt = 30 + _random.NextDouble() * 45 * tempo;

        // A duel or a grenade takes a moment, and nothing else starts until
        // it is over, so no one acts after a death that already happened.
        double clock = 0;

        while (true)
        {
            double throwAt =
                nextThrow < throws.Count ? throws[nextThrow].At : double.PositiveInfinity;
            double fightAt =
                Alive(Side.T).Count > 0 && Alive(Side.CT).Count > 0
                    ? nextFight
                    : double.PositiveInfinity;
            double plantCheck =
                !_planted && plantAt.HasValue ? plantAt.Value : double.PositiveInfinity;
            double defuseAt = _defuseAt ?? double.PositiveInfinity;
            double timeout = _planted ? double.PositiveInfinity : _setup.RoundTime;

            double next = new[]
            {
                throwAt,
                fightAt,
                plantCheck,
                defuseAt,
                _explodeAt,
                timeout,
            }.Min();
            double now = Math.Max(next, clock);

            if (next == _explodeAt)
            {
                Objective(now, "Exploded", _planter!, includeSite: true);
                return End(now, Side.T, eWinReason.BombExploded);
            }

            if (next == timeout)
            {
                return End(now, Side.CT, eWinReason.TimeRanOut);
            }

            if (next == defuseAt)
            {
                if (TryDefuse(now))
                {
                    return End(now, Side.CT, eWinReason.BombDefused);
                }
                clock = now;
                continue;
            }

            if (next == plantCheck)
            {
                plantAt = null;
                TryPlant(now);
                clock = now;
                continue;
            }

            if (next == throwAt)
            {
                (_, SimPlayer thrower, string type) = throws[nextThrow++];
                clock = Throw(thrower, type, now);
            }
            else
            {
                clock = Fight(now);
                nextFight = clock + (2 + _random.NextDouble() * 12) * tempo;
            }

            RoundOutcome? ended = CheckElimination(clock);
            if (ended != null)
            {
                return ended;
            }
        }
    }

    private RoundOutcome? CheckElimination(double now)
    {
        if (Alive(Side.CT).Count == 0)
        {
            return End(now, Side.T, eWinReason.TerroristsWin);
        }

        if (Alive(Side.T).Count > 0)
        {
            return null;
        }

        if (!_planted)
        {
            return End(now, Side.CT, eWinReason.CTsWin);
        }

        // nobody left to stop a defuse, if there is time for one
        double attempt = now + 2 + _random.NextDouble() * 8;
        if (attempt < _explodeAt - 5 && (_defuseAt == null || attempt < _defuseAt))
        {
            _defuseAt = attempt;
        }

        return null;
    }

    private RoundOutcome End(double now, Side winner, eWinReason reason)
    {
        return new RoundOutcome
        {
            Winner = winner,
            Reason = reason,
            Duration = now,
            Events = _events.OrderBy(planned => planned.At).ToList(),
            BombPlanted = _planted,
            KillRewards = _killRewards,
        };
    }

    private void TryPlant(double now)
    {
        List<SimPlayer> terrorists = Alive(Side.T);
        List<SimPlayer> counterTerrorists = Alive(Side.CT);

        if (terrorists.Count == 0 || counterTerrorists.Count == 0)
        {
            return;
        }

        double chance = terrorists.Count >= counterTerrorists.Count ? 0.8 : 0.35;
        if (_random.NextDouble() > chance)
        {
            return;
        }

        _planted = true;
        _planter = terrorists[_random.Next(terrorists.Count)];
        _explodeAt = now + _setup.BombTimer;
        _defuseAt = now + 8 + _random.NextDouble() * (_setup.BombTimer - 14);

        Objective(now, "Planted", _planter);
    }

    private bool TryDefuse(double now)
    {
        List<SimPlayer> counterTerrorists = Alive(Side.CT);

        if (counterTerrorists.Count == 0)
        {
            _defuseAt = null;
            return false;
        }

        bool unopposed = Alive(Side.T).Count == 0;

        if (_random.NextDouble() < (unopposed ? 0.85 : 0.15))
        {
            Objective(now, "Defused", counterTerrorists[_random.Next(counterTerrorists.Count)]);
            return true;
        }

        double retry = now + 4 + _random.NextDouble() * 4;
        _defuseAt = !unopposed && retry < _explodeAt - 5 ? retry : null;

        return false;
    }

    private double Strength(SimPlayer player, double now)
    {
        double gear = player.Loadout.Primary.Price switch
        {
            >= 4750 => 1.1,
            >= 2700 => 1.0,
            >= 1700 => 0.85,
            >= 1000 => 0.75,
            >= 700 => 0.65,
            _ => 0.55,
        };

        if (player.Armor > 0 && gear < 0.7)
        {
            gear += 0.1;
        }

        bool blind = _blinded.TryGetValue(player.SteamId, out var flash) && flash.Until > now;
        bool defending = _sides[player.SteamId] == (_planted ? Side.T : Side.CT);

        return player.Skill * gear * (blind ? 0.4 : 1) * (defending ? (_planted ? 1.3 : 1.05) : 1);
    }

    private double Fight(double now)
    {
        List<SimPlayer> terrorists = Alive(Side.T);
        List<SimPlayer> counterTerrorists = Alive(Side.CT);

        SimPlayer terrorist = terrorists[_random.Next(terrorists.Count)];
        SimPlayer counterTerrorist = counterTerrorists[_random.Next(counterTerrorists.Count)];

        double terroristStrength = Strength(terrorist, now);
        double counterTerroristStrength = Strength(counterTerrorist, now);

        bool terroristWins =
            _random.NextDouble()
            < terroristStrength / (terroristStrength + counterTerroristStrength);

        SimPlayer winner = terroristWins ? terrorist : counterTerrorist;
        SimPlayer loser = terroristWins ? counterTerrorist : terrorist;
        double at = now;

        if (_random.NextDouble() < 0.35)
        {
            at = Shoot(loser, winner, loser.Loadout.Primary, at, Intent.Wound, 1 + _random.Next(2));
        }

        List<SimPlayer> mates = (terroristWins ? terrorists : counterTerrorists)
            .Where(player => player != winner)
            .ToList();
        if (mates.Count > 0 && _random.NextDouble() < 0.3)
        {
            SimPlayer mate = mates[_random.Next(mates.Count)];
            at = Shoot(mate, loser, mate.Loadout.Primary, at, Intent.Wound, 1 + _random.Next(2));
        }

        return Shoot(winner, loser, winner.Loadout.Primary, at, Intent.Kill, 4);
    }

    private enum Intent
    {
        Wound,
        Hurt,
        Kill,
    }

    // Kill keeps shooting until the victim dies, Wound stops short of it, and
    // Hurt lands what it lands.
    private double Shoot(
        SimPlayer attacker,
        SimPlayer victim,
        Weapon weapon,
        double at,
        Intent intent,
        int hits
    )
    {
        bool grenade = weapon == Weapons.HeGrenade || weapon == Weapons.Inferno;
        double headRate =
            grenade ? 0
            : weapon.Scoped ? 0.15
            : weapon.Price >= 1700 ? 0.4
            : 0.3;
        string hitgroup = "Body";

        for (int hit = 0; hit < hits && victim.Alive; hit++)
        {
            bool head = _random.NextDouble() < headRate;
            hitgroup =
                head ? "Head"
                : grenade ? "Body"
                : BodyHitgroups[_random.Next(BodyHitgroups.Length)];
            int raw = head ? weapon.Head : _random.Next(weapon.BodyMin, weapon.BodyMax + 1);

            if (intent == Intent.Wound)
            {
                raw = Math.Min(raw, victim.Health - 1);
                if (raw <= 0)
                {
                    break;
                }
            }

            at += 0.05 + _random.NextDouble() * 0.2;
            Damage(attacker, victim, weapon, raw, hitgroup, at);
        }

        if (intent == Intent.Kill && victim.Alive)
        {
            at += 0.05;
            Damage(attacker, victim, weapon, victim.Health, hitgroup, at);
        }

        if (!victim.Alive)
        {
            Kill(attacker, victim, weapon, hitgroup, at);
        }

        return at;
    }

    private void Damage(
        SimPlayer attacker,
        SimPlayer victim,
        Weapon weapon,
        int raw,
        string hitgroup,
        double at
    )
    {
        int applied = Math.Min(raw, victim.Health);
        int armorDamage =
            victim.Armor > 0 && !hitgroup.EndsWith("Leg")
                ? Math.Min(victim.Armor, 1 + _random.Next(Math.Max(1, raw / 4)))
                : 0;

        victim.Health -= applied;
        victim.Armor -= armorDamage;

        if (!_damageTaken.TryGetValue(victim.SteamId, out var byAttacker))
        {
            _damageTaken[victim.SteamId] = byAttacker = new Dictionary<string, int>();
        }
        byAttacker[attacker.SteamId] = byAttacker.GetValueOrDefault(attacker.SteamId) + applied;

        Add(
            at,
            "damage",
            new Dictionary<string, object>
            {
                { "attacker_steam_id", attacker.SteamId },
                { "attacker_team", SideRules.ToApi(_sides[attacker.SteamId]) },
                { "attacker_location", Callouts.Pick(_setup.MapName, _random) },
                { "attacker_location_coordinates", Coordinates() },
                { "weapon", weapon.Name },
                { "damage", applied },
                { "damage_armor", armorDamage },
                { "hitgroup", hitgroup },
                { "health", victim.Health },
                { "armor", victim.Armor },
                { "attacked_steam_id", victim.SteamId },
                { "attacked_team", SideRules.ToApi(_sides[victim.SteamId]) },
                { "attacked_location", Callouts.Pick(_setup.MapName, _random) },
                { "attacked_location_coordinates", Coordinates() },
            }
        );
    }

    private void Kill(
        SimPlayer attacker,
        SimPlayer victim,
        Weapon weapon,
        string hitgroup,
        double at
    )
    {
        bool blinded = _blinded.TryGetValue(attacker.SteamId, out var flash) && flash.Until > at;

        Add(
            at + 0.001,
            "kill",
            new Dictionary<string, object>
            {
                { "no_scope", weapon.Scoped && _random.NextDouble() < 0.05 },
                { "blinded", blinded },
                { "thru_smoke", _random.NextDouble() < 0.05 },
                { "thru_wall", _random.NextDouble() < 0.07 },
                { "headshot", hitgroup == "Head" },
                { "attacker_steam_id", attacker.SteamId },
                { "attacker_team", SideRules.ToApi(_sides[attacker.SteamId]) },
                { "attacker_location", Callouts.Pick(_setup.MapName, _random) },
                { "attacker_location_coordinates", Coordinates() },
                { "weapon", weapon.Name },
                { "hitgroup", hitgroup },
                { "attacked_steam_id", victim.SteamId },
                { "attacked_team", SideRules.ToApi(_sides[victim.SteamId]) },
                { "attacked_location", Callouts.Pick(_setup.MapName, _random) },
                { "attacked_location_coordinates", Coordinates() },
            }
        );

        _killRewards[attacker.SteamId] =
            _killRewards.GetValueOrDefault(attacker.SteamId) + weapon.KillReward;

        Assist(attacker, victim, at + 0.002);
    }

    // CS2 credits a teammate who did more than 40 damage, or failing that the
    // teammate whose flash the victim died in.
    private void Assist(SimPlayer attacker, SimPlayer victim, double at)
    {
        Side attackerSide = _sides[attacker.SteamId];
        SimPlayer? assister = null;
        bool flash = false;

        if (_damageTaken.TryGetValue(victim.SteamId, out var byAttacker))
        {
            string? helper = byAttacker
                .Where(entry =>
                    entry.Key != attacker.SteamId
                    && entry.Value > 40
                    && _sides[entry.Key] == attackerSide
                )
                .OrderByDescending(entry => entry.Value)
                .Select(entry => entry.Key)
                .FirstOrDefault();

            assister = helper == null ? null : Find(helper);
        }

        if (
            assister == null
            && _blinded.TryGetValue(victim.SteamId, out var blind)
            && blind.Until > at
            && blind.By != attacker
            && _sides[blind.By.SteamId] == attackerSide
        )
        {
            assister = blind.By;
            flash = true;
        }

        if (assister == null)
        {
            return;
        }

        Add(
            at,
            "assist",
            new Dictionary<string, object>
            {
                { "attacker_steam_id", assister.SteamId },
                { "attacker_team", SideRules.ToApi(attackerSide) },
                { "attacked_steam_id", victim.SteamId },
                { "attacked_team", SideRules.ToApi(_sides[victim.SteamId]) },
                { "flash", flash },
            }
        );
    }

    private double Throw(SimPlayer thrower, string type, double at)
    {
        if (!thrower.Alive)
        {
            return at;
        }

        double end = at;

        Side side = _sides[thrower.SteamId];
        List<SimPlayer> enemies = Alive(SideRules.Opposite(side));
        List<SimPlayer> mates = Alive(side).Where(player => player != thrower).ToList();

        Add(
            at,
            "utility",
            new Dictionary<string, object>
            {
                { "type", type },
                { "attacker_steam_id", thrower.SteamId },
                {
                    "attacker_location_coordinates",
                    string.Join(
                        ",",
                        Enumerable
                            .Range(0, 3)
                            .Select(axis =>
                                (
                                    _random.NextDouble() * (axis == 2 ? 400 : 4800)
                                    - (axis == 2 ? 200 : 2400)
                                ).ToString("0.######", CultureInfo.InvariantCulture)
                            )
                    )
                },
            }
        );

        switch (type)
        {
            case "Flash":
                foreach (
                    SimPlayer victim in Sample(enemies, _random.Next(3))
                        .Concat(Sample(mates, _random.NextDouble() < 0.2 ? 1 : 0))
                )
                {
                    double duration = Math.Round(0.3 + _random.NextDouble() * 4.2, 2);
                    bool teamFlash = _sides[victim.SteamId] == side;

                    if (!teamFlash)
                    {
                        _blinded[victim.SteamId] = (thrower, at + duration);
                    }

                    end = at + 0.01;
                    Add(
                        end,
                        "flash",
                        new Dictionary<string, object>
                        {
                            { "attacker_steam_id", thrower.SteamId },
                            { "attacked_steam_id", victim.SteamId },
                            { "duration", duration },
                            { "team_flash", teamFlash },
                        }
                    );
                }
                break;
            case "HighExplosive":
                foreach (SimPlayer victim in Sample(enemies, _random.Next(3)))
                {
                    end = Math.Max(
                        end,
                        Shoot(thrower, victim, Weapons.HeGrenade, at, Intent.Hurt, 1)
                    );
                }
                break;
            case "Molotov":
                foreach (SimPlayer victim in Sample(enemies, _random.Next(2)))
                {
                    end = Math.Max(
                        end,
                        Shoot(
                            thrower,
                            victim,
                            Weapons.Inferno,
                            at + 1,
                            Intent.Wound,
                            1 + _random.Next(3)
                        )
                    );
                }
                break;
        }

        return end;
    }

    private void Objective(double at, string type, SimPlayer player, bool includeSite = false)
    {
        var data = new Dictionary<string, object>
        {
            { "type", type },
            { "player_steam_id", player.SteamId },
        };

        if (includeSite)
        {
            data["site"] = _random.Next(2);
        }

        Add(at, "objective", data);
    }

    private void Add(double at, string name, Dictionary<string, object> data)
    {
        data["round"] = _setup.Round;
        data["match_map_id"] = _setup.MatchMapId;
        _events.Add(new PlannedEvent(at, name, data));
    }

    private SimPlayer Find(string steamId)
    {
        return _setup
            .Terrorists.Concat(_setup.CounterTerrorists)
            .First(player => player.SteamId == steamId);
    }

    private IEnumerable<SimPlayer> Sample(List<SimPlayer> players, int count)
    {
        return players.OrderBy(_ => _random.Next()).Take(count).ToList();
    }

    private string Coordinates()
    {
        return $"{_random.Next(-2400, 2400)} {_random.Next(-2400, 2400)} {_random.Next(-200, 200)}";
    }
}
