namespace PerformativeMail.Sim.World;

/// <summary>Category of a <see cref="StaticSolid"/> (ADR-0026 section 5; populated by world-stub story 003).</summary>
public enum SolidKind : byte
{
    /// <summary>A lot's house.</summary>
    House = 0,

    /// <summary>The Post Office building.</summary>
    PostOffice = 1,

    /// <summary>A prop.</summary>
    Prop = 2,

    /// <summary>A roof volume.</summary>
    Roof = 3,
}
