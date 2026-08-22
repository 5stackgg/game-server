namespace FiveStack.Enums;

// The order a drill hands out lineups.
public enum eDrillOrder
{
    // A shuffled pass through the book, which is the honest default: at the
    // start of a session the panel's progress says nothing, so ordering by it
    // would only mean "alphabetical" while pretending to mean something.
    Random,

    // The ones the panel says are going worst, first.
    Worst,
}
