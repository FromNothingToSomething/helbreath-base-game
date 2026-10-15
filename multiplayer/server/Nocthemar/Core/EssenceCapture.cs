namespace Server.Nocthemar.Core;

/// <summary>How an essence orb is picked up.</summary>
public enum EssenceCapture : byte {
    /// <summary>Nothing needed (not an orb).</summary>
    None = 0,

    /// <summary>A capture passive does it: no vial is spent.</summary>
    Passive = 1,

    /// <summary>One Essence Vial is consumed.</summary>
    UseVial = 2,

    /// <summary>No vial and no passive: the orb stays on the ground.</summary>
    Refused = 3,
}

public static class EssenceCaptureRules {
    /// <summary>Capture needed for an orb: the passive first (it spends nothing), else one vial, else refused.</summary>
    public static EssenceCapture Decide(bool hasCapturePassive, bool hasVial) =>
        hasCapturePassive ? EssenceCapture.Passive : hasVial ? EssenceCapture.UseVial : EssenceCapture.Refused;
}
