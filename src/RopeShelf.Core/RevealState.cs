namespace RopeShelf.Core;

public enum RevealMode { Hidden, HoverPending, AutoPeek, Revealed }

/// <summary>All timers feed one state machine, including during nested menu/drag message loops.</summary>
public sealed class RevealState
{
    public RevealMode Mode { get; private set; }
    public bool Visible => Mode is RevealMode.AutoPeek or RevealMode.Revealed;
    public int Holds { get; private set; }
    private DateTimeOffset deadline;
    private DateTimeOffset? leftAt;
    private bool explicitReveal;
    private bool hoverArmed = true;

    public void Hold() => Holds++;
    public void Release() => Holds = Math.Max(0, Holds - 1);
    public void Hide() { if (Holds > 0) return; Mode = RevealMode.Hidden; explicitReveal = false; leftAt = null; hoverArmed = false; }
    public void Show(DateTimeOffset now, bool automatic)
    {
        if (automatic && Visible)
        {
            if (Mode == RevealMode.AutoPeek) deadline = now.AddMilliseconds(1500);
            return;
        }
        Mode = automatic ? RevealMode.AutoPeek : RevealMode.Revealed;
        explicitReveal = !automatic;
        deadline = now.AddMilliseconds(1500);
        leftAt = null;
    }
    public void Tick(DateTimeOffset now, bool hotZone, bool inside, bool fullscreen, bool hasItems)
    {
        if (!hotZone) hoverArmed = true;
        if (!hasItems) { Hide(); return; }
        if (Holds > 0) return;
        if (fullscreen && !explicitReveal) { Hide(); return; }
        if (!Visible)
        {
            if (fullscreen || !hotZone || !hoverArmed) { Mode = RevealMode.Hidden; return; }
            if (Mode != RevealMode.HoverPending) { Mode = RevealMode.HoverPending; deadline = now.AddMilliseconds(300); }
            else if (now >= deadline) { Mode = RevealMode.Revealed; explicitReveal = false; leftAt = null; }
            return;
        }
        if (inside || hotZone) { leftAt = null; if (Mode == RevealMode.AutoPeek) Mode = RevealMode.Revealed; return; }
        if (Mode == RevealMode.AutoPeek) { if (now >= deadline) Hide(); return; }
        leftAt ??= now;
        if (now - leftAt >= TimeSpan.FromMilliseconds(350)) Hide();
    }
}
