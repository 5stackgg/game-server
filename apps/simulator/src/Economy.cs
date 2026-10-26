using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Simulator;

public sealed record Weapon(
    string Name,
    int Price,
    int BodyMin,
    int BodyMax,
    int Head,
    int KillReward,
    bool Scoped = false
);

public static class Weapons
{
    public static readonly Weapon Glock = new("glock", 0, 13, 25, 70, 300);
    public static readonly Weapon Usp = new("usp_silencer", 0, 18, 35, 100, 300);
    public static readonly Weapon P2000 = new("hkp2000", 0, 18, 35, 100, 300);
    public static readonly Weapon P250 = new("p250", 300, 25, 38, 120, 300);
    public static readonly Weapon Tec9 = new("tec9", 500, 20, 33, 120, 300);
    public static readonly Weapon FiveSeven = new("fiveseven", 500, 20, 32, 120, 300);
    public static readonly Weapon Deagle = new("deagle", 700, 40, 55, 230, 300);
    public static readonly Weapon Mac10 = new("mac10", 1050, 18, 29, 70, 600);
    public static readonly Weapon Mp9 = new("mp9", 1250, 16, 26, 65, 600);
    public static readonly Weapon Ump45 = new("ump45", 1200, 20, 35, 100, 600);
    public static readonly Weapon Galil = new("galilar", 1800, 25, 30, 99, 300);
    public static readonly Weapon Famas = new("famas", 2050, 20, 30, 90, 300);
    public static readonly Weapon Ssg08 = new("ssg08", 1700, 70, 88, 200, 300, true);
    public static readonly Weapon Ak47 = new("ak47", 2700, 27, 36, 111, 300);
    public static readonly Weapon M4A1S = new("m4a1_silencer", 2900, 30, 38, 92, 300);
    public static readonly Weapon M4A4 = new("m4a1", 3100, 26, 33, 92, 300);
    public static readonly Weapon Awp = new("awp", 4750, 85, 115, 200, 100, true);
    public static readonly Weapon HeGrenade = new("hegrenade", 300, 5, 57, 57, 300);
    public static readonly Weapon Inferno = new("inferno", 0, 3, 8, 8, 300);
}

public enum BuyTier
{
    Pistol,
    Eco,
    Force,
    Full,
}

public sealed record Loadout(Weapon Primary, int ArmorValue, bool Helmet, List<string> Grenades)
{
    public static Loadout Pistol(Side side)
    {
        return new Loadout(side == Side.T ? Weapons.Glock : Weapons.Usp, 0, false, []);
    }
}

public static class Economy
{
    public const int StartMoney = 800;
    public const int OvertimeStartMoney = 12500;

    public static int MaxMoney(MatchOptions options)
    {
        return options.type == "Wingman" || options.type == "Duel" ? 8000 : 16000;
    }

    public static int WinReward(eWinReason reason)
    {
        return reason == eWinReason.BombExploded || reason == eWinReason.BombDefused ? 3500 : 3250;
    }

    public static int LossBonus(int lossStreak)
    {
        return Math.Min(1400 + 500 * Math.Max(0, lossStreak - 1), 3400);
    }

    public static BuyTier TeamTier(IReadOnlyList<SimPlayer> team, bool pistolRound)
    {
        if (pistolRound)
        {
            return BuyTier.Pistol;
        }

        double average = team.Count == 0 ? 0 : team.Average(player => player.Money);

        if (average >= 4100)
        {
            return BuyTier.Full;
        }

        return average >= 2200 ? BuyTier.Force : BuyTier.Eco;
    }

    // Spends each player's money on the team's buy and hands back what they
    // carry into the round.
    public static void Buy(IReadOnlyList<SimPlayer> team, Side side, BuyTier tier, Random random)
    {
        bool awpBought = false;

        foreach (SimPlayer player in team.OrderByDescending(player => player.Money))
        {
            var grenades = new List<string>();
            Weapon primary =
                side == Side.T ? Weapons.Glock
                : random.Next(2) == 0 ? Weapons.Usp
                : Weapons.P2000;
            int armor = 0;
            bool helmet = false;
            int budget = player.Money;

            void Take(Weapon weapon)
            {
                if (budget >= weapon.Price)
                {
                    primary = weapon;
                    budget -= weapon.Price;
                }
            }

            void Grenade(string type, int price)
            {
                if (budget >= price)
                {
                    grenades.Add(type);
                    budget -= price;
                }
            }

            switch (tier)
            {
                case BuyTier.Pistol:
                    if (random.Next(3) == 0 && budget >= 650)
                    {
                        armor = 100;
                        budget -= 650;
                    }
                    else if (random.Next(2) == 0)
                    {
                        Take(Weapons.P250);
                        Grenade("Flash", 200);
                    }
                    break;
                case BuyTier.Eco:
                    if (random.Next(3) == 0)
                    {
                        Take(side == Side.T ? Weapons.Tec9 : Weapons.FiveSeven);
                    }
                    break;
                case BuyTier.Force:
                    Take(
                        side == Side.T
                            ? random.Next(2) == 0
                                ? Weapons.Galil
                                : Weapons.Mac10
                            : random.Next(2) == 0
                                ? Weapons.Famas
                                : Weapons.Mp9
                    );
                    if (budget >= 650)
                    {
                        armor = 100;
                        budget -= 650;
                    }
                    Grenade("Flash", 200);
                    break;
                case BuyTier.Full:
                    if (!awpBought && budget >= Weapons.Awp.Price + 1000 && random.Next(3) == 0)
                    {
                        awpBought = true;
                        Take(Weapons.Awp);
                    }
                    else
                    {
                        Take(
                            side == Side.T ? Weapons.Ak47
                            : random.Next(3) == 0 ? Weapons.M4A4
                            : Weapons.M4A1S
                        );
                    }
                    if (budget >= 1000)
                    {
                        armor = 100;
                        helmet = true;
                        budget -= 1000;
                    }
                    Grenade("Smoke", 300);
                    Grenade("Flash", 200);
                    Grenade("Molotov", side == Side.T ? 400 : 500);
                    if (random.Next(2) == 0)
                    {
                        Grenade("HighExplosive", 300);
                    }
                    break;
            }

            player.Money = Math.Max(0, budget);
            player.Health = 100;
            player.Armor = armor;
            player.Loadout = new Loadout(primary, armor, helmet, grenades);
        }
    }
}
