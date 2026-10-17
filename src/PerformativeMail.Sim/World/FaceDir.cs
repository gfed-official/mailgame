namespace PerformativeMail.Sim.World;

/// <summary>One of four axis directions a lot faces, so catch volumes stay axis-aligned (ADR-0026 section 6).</summary>
public enum FaceDir : byte
{
    /// <summary>Faces +X.</summary>
    PosX = 0,

    /// <summary>Faces -X.</summary>
    NegX = 1,

    /// <summary>Faces +Z.</summary>
    PosZ = 2,

    /// <summary>Faces -Z.</summary>
    NegZ = 3,
}
