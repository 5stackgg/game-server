namespace FiveStack.Enums;

// Mirrors public.e_utility_types. The names are the API's spelling, not the
// engine's: the demo parser emits "HE" for HighExplosive and anything that
// forgets to map it silently drops every HE lineup.
public enum eUtilityType
{
    Decoy,
    HighExplosive,
    Flash,
    Molotov,
    Smoke,
}
