using System.Runtime.CompilerServices;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The 32 opaque bytes an owner writes through <see cref="IInteractable.WritePrompt"/> for the HUD prompt (ADR-0014 section 7).
/// Players carries them into the interaction snapshot and never reads them. A fixed inline array: no allocation, blittable.
/// </summary>
/// <example><code>args[0] = 1;   // the owner's prompt variant</code></example>
[InlineArray(Size)]
public struct PromptArgs
{
    /// <summary>The size in bytes.</summary>
    public const int Size = 32;

#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private byte _e0;
#pragma warning restore CS0169
}
