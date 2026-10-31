using FiveStack.Utilities;
using Xunit;

public class CommunityWeaponsTests
{
    [Theory]
    [InlineData("weapon_ak47", "ak47")]
    [InlineData("ak47", "ak47")]
    [InlineData("AK47", "ak47")]
    [InlineData("weapon_knife", "knife")]
    [InlineData("knife_t", "knife")]
    [InlineData("knife_karambit", "knife")]
    [InlineData("weapon_knife_butterfly", "knife")]
    [InlineData("bayonet", "knife")]
    [InlineData("weapon_bayonet", "knife")]
    [InlineData("hegrenade", "hegrenade")]
    [InlineData("inferno", "inferno")]
    [InlineData("taser", "taser")]
    [InlineData(" weapon_m4a1_silencer ", "m4a1_silencer")]
    [InlineData("weapon_m4a1_silencer_off", "m4a1_silencer")]
    [InlineData("usp_silencer_off", "usp_silencer")]
    [InlineData("weapon_usp_silencer", "usp_silencer")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void WeaponsAreNamedAsTheMatchStatsNameThem(string? raw, string expected)
    {
        Assert.Equal(expected, CommunityWeapons.Normalize(raw));
    }

    [Theory]
    [InlineData("weapon_ak47", true)]
    [InlineData("deagle", true)]
    [InlineData("nova", true)]
    [InlineData("m4a1_silencer", true)]
    [InlineData("knife", false)]
    [InlineData("knife_t", false)]
    [InlineData("bayonet", false)]
    [InlineData("weapon_hegrenade", false)]
    [InlineData("flashbang", false)]
    [InlineData("smokegrenade", false)]
    [InlineData("molotov", false)]
    [InlineData("incgrenade", false)]
    [InlineData("decoy", false)]
    [InlineData("inferno", false)]
    [InlineData("taser", false)]
    [InlineData("weapon_c4", false)]
    [InlineData("planted_c4", false)]
    [InlineData("healthshot", false)]
    [InlineData("world", false)]
    [InlineData("worldent", false)]
    [InlineData("fists", false)]
    [InlineData("weapon_fists", false)]
    [InlineData("snowball", false)]
    [InlineData("bumpmine", false)]
    [InlineData("breachcharge", false)]
    [InlineData("shield", false)]
    [InlineData("tablet", false)]
    [InlineData("firebomb", false)]
    [InlineData("trigger_hurt", false)]
    [InlineData("tagrenade", false)]
    [InlineData("hammer", false)]
    [InlineData("gun0", false)]
    [InlineData("usp_silencer", true)]
    [InlineData("usp_silencer_off", true)]
    [InlineData("m4a1_silencer_off", true)]
    [InlineData("hkp2000", true)]
    [InlineData("cz75a", true)]
    [InlineData("revolver", true)]
    [InlineData("mp5sd", true)]
    [InlineData("m4a1", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyGunsAreFirearms(string? weapon, bool expected)
    {
        Assert.Equal(expected, CommunityWeapons.IsFirearm(weapon));
    }

    [Theory]
    [InlineData("knife", CommunityStatKeys.KnifeKills)]
    [InlineData("knife_t", CommunityStatKeys.KnifeKills)]
    [InlineData("bayonet", CommunityStatKeys.KnifeKills)]
    [InlineData("taser", CommunityStatKeys.ZeusKills)]
    [InlineData("hegrenade", CommunityStatKeys.HeKills)]
    [InlineData("inferno", CommunityStatKeys.MolotovKills)]
    [InlineData("molotov", CommunityStatKeys.MolotovKills)]
    [InlineData("ak47", null)]
    [InlineData("flashbang", null)]
    [InlineData("", null)]
    public void KillTypesGoByTheWeapon(string weapon, string? expected)
    {
        Assert.Equal(expected, CommunityWeapons.KillType(weapon));
    }

    [Theory]
    [InlineData("hegrenade", CommunityStatKeys.HeDamage)]
    [InlineData("inferno", CommunityStatKeys.MolotovDamage)]
    [InlineData("molotov", CommunityStatKeys.MolotovDamage)]
    [InlineData("ak47", null)]
    [InlineData("knife", null)]
    public void UtilityDamageGoesByTheWeapon(string weapon, string? expected)
    {
        Assert.Equal(expected, CommunityWeapons.DamageType(weapon));
    }

    // CS2's item definition indexes, which tell apart the guns that share a
    // class with another.
    [Theory]
    [InlineData(60, "m4a1_silencer")]
    [InlineData(16, "m4a1")]
    [InlineData(61, "usp_silencer")]
    [InlineData(32, "hkp2000")]
    [InlineData(63, "cz75a")]
    [InlineData(36, "p250")]
    [InlineData(64, "revolver")]
    [InlineData(1, "deagle")]
    [InlineData(23, "mp5sd")]
    [InlineData(33, "mp7")]
    [InlineData(7, "ak47")]
    [InlineData(42, null)]
    [InlineData(31, null)]
    [InlineData(0, null)]
    public void AnItemDefinitionNamesItsGun(int index, string? expected)
    {
        Assert.Equal(expected, CommunityWeapons.FirearmByDefinition(index));
    }

    [Theory]
    [InlineData("weapon_m4a1", true)]
    [InlineData("hkp2000", true)]
    [InlineData("p250", true)]
    [InlineData("deagle", true)]
    [InlineData("mp7", true)]
    [InlineData("m4a1_silencer", false)]
    [InlineData("ak47", false)]
    [InlineData("knife", false)]
    public void SomeGunsShareTheirClassWithAnother(string weapon, bool expected)
    {
        Assert.Equal(expected, CommunityWeapons.SharesAClass(weapon));
    }
}
