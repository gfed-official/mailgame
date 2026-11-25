using System.Collections.Generic;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// The debug world's showcase (content/maps/debug_world.json, <c>--debug-world</c>; design
    /// choice 119): a map whose "debug" block puts every construct there is up, running, as each
    /// run starts, and turns the debug toggles on. Kept as the file says it, by ids and names;
    /// <see cref="SimWorld"/> resolves it against the content when it builds the showcase.
    /// </summary>
    public sealed class DebugWorldDef
    {
        /// <summary>The shift the run is in: late enough for every blueprint, the captain and Cargo.</summary>
        public int StartShift { get; init; } = 5;
        /// <summary>One piece of mail into the Intake this often, the addresses in turn (instead of the spawner's batches).</summary>
        public double MailEverySeconds { get; init; } = 1.0;
        /// <summary>Nobody of the team takes damage.</summary>
        public bool GodMode { get; init; } = true;
        /// <summary>The wallet stays full, every blueprint is owned and building takes no materials.</summary>
        public bool Unlimited { get; init; } = true;
        /// <summary>The Delivery clock stands.</summary>
        public bool NoTimer { get; init; } = true;
        /// <summary>Night raids come as in a run; off unless a player asks for them (<c>--raid</c>).</summary>
        public bool Raids { get; init; }
        public IReadOnlyList<ShowcaseConstruct> Constructs { get; init; } = new List<ShowcaseConstruct>();
        public IReadOnlyList<ShowcaseVehicle> Vehicles { get; init; } = new List<ShowcaseVehicle>();
        /// <summary>The tiles of the depots and ports whose crew is hired as the run starts.</summary>
        public IReadOnlyList<(int X, int Z)> Hires { get; init; } = new List<(int, int)>();
        /// <summary>The stations, in the order of their camera bookmarks.</summary>
        public IReadOnlyList<ShowcaseStation> Stations { get; init; } = new List<ShowcaseStation>();
        /// <summary>The turret range's practice raiders (raids or not): where each stands up, and how often once the last is gone.</summary>
        public IReadOnlyList<ShowcaseTarget> Targets { get; init; } = new List<ShowcaseTarget>();
    }

    /// <summary>A raider of <see cref="Enemy"/> put on tile (X, Z) in Delivery, again <see cref="EverySeconds"/> after the last one fell or went.</summary>
    public sealed class ShowcaseTarget
    {
        public string Enemy { get; init; } = "";
        public int X { get; init; }
        public int Z { get; init; }
        public double EverySeconds { get; init; } = 4.0;
    }

    /// <summary>One construct of the showcase, as a carrier would build it on (X, Z) turned so.</summary>
    public sealed class ShowcaseConstruct
    {
        public string Building { get; init; } = "";
        public int X { get; init; }
        public int Z { get; init; }
        public int Rotation { get; init; }
        /// <summary>Its filters (a sorter's outputs, a console's stops), each "street:NAME", "district:N", "kind:ID", "address:NUMBER STREET" or "" for none.</summary>
        public IReadOnlyList<string> Filters { get; init; } = new List<string>();
    }

    /// <summary>A vehicle standing in the bay of the depot or port built on (BayX, BayZ), with this much fuel (seconds; below 0 a full tank).</summary>
    public sealed class ShowcaseVehicle
    {
        public string Vehicle { get; init; } = "";
        public int BayX { get; init; }
        public int BayZ { get; init; }
        public float FuelSeconds { get; init; } = -1f;
    }

    /// <summary>A station of the showcase: where its camera bookmark looks from and at, and what shows that mail goes through it.</summary>
    public sealed class ShowcaseStation
    {
        public string Name { get; init; } = "";
        public float EyeX { get; init; }
        public float EyeY { get; init; }
        public float EyeZ { get; init; }
        public float TargetX { get; init; }
        public float TargetY { get; init; }
        public float TargetZ { get; init; }
        public IReadOnlyList<ShowcaseProof> Proofs { get; init; } = new List<ShowcaseProof>();
    }

    public enum ShowcaseProofKind : byte
    {
        /// <summary>A piece of mail came onto the belt segment that runs over tile (X, Z).</summary>
        Belt = 0,
        /// <summary>A machine delivered a piece (to <see cref="ShowcaseProof.To"/> if given).</summary>
        Delivered = 1,
        /// <summary>An Oil Pump filled an Oil Can.</summary>
        Pumped = 2,
        /// <summary>The turret on tile (X, Z) fired.</summary>
        Fired = 3,
    }

    public sealed class ShowcaseProof
    {
        public ShowcaseProofKind Kind { get; init; }
        public int X { get; init; }
        public int Z { get; init; }
        /// <summary>For <see cref="ShowcaseProofKind.Delivered"/>: the machine's name ("belt", "inserter", "pipe", "driver", "dock", "captain").</summary>
        public string Machine { get; init; } = "";
        /// <summary>For <see cref="ShowcaseProofKind.Delivered"/>: the address ("1 Anvil Close"); empty for any.</summary>
        public string To { get; init; } = "";

        public override string ToString() => Kind switch
        {
            ShowcaseProofKind.Belt => $"mail on the belt at ({X}, {Z})",
            ShowcaseProofKind.Delivered => $"{("aeiou".IndexOf(Machine.Length > 0 ? Machine[0] : 'x') >= 0 ? "an" : "a")} {Machine} delivery{(To.Length > 0 ? " to " + To : "")}",
            ShowcaseProofKind.Fired => $"a shot of the turret at ({X}, {Z})",
            _ => "an Oil Can pumped",
        };
    }
}
