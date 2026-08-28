using FiveStack.Utilities;

namespace UtilityPractice;

public abstract class HudTemplate
{
    public abstract HudLayoutSlots Slots { get; }

    // False for anything that sits on screen while the player is aiming. A
    // passive panel that captures input takes the cursor and the shot with it.
    public virtual bool CapturesInput => false;

    public abstract void Apply(HudSurface surface);

    // Turns a raw button id from the layout into whatever the caller wanted to
    // hear back, so row indices never leak out of the template.
    public virtual string Resolve(string buttonId)
    {
        return buttonId;
    }
}
