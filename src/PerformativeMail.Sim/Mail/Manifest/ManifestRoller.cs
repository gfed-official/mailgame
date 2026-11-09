using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>Negative result codes of <see cref="ManifestRoller.Roll"/> beyond the F3/F2/F4 codes it passes through.</summary>
public static class ManifestRollStatus
{
    /// <summary>More addresses than the manifest was sized for.</summary>
    public const int TooManyAddresses = -20;

    /// <summary>F3 gave more units than <see cref="ShiftManifest.MaxUnits"/>.</summary>
    public const int TooManyUnits = -21;

    /// <summary>The kind tables are longer than <see cref="KindCounts.MaxKinds"/> or do not match.</summary>
    public const int InvalidKinds = -22;
}

/// <summary>
/// The manifest roller (GDD mail-destinations R10 and Determinism; ADR-0017 section 10, ADR-0005; mail-destinations story 013): a pure
/// function of (stream, <see cref="SpawnProfile"/>, unlocked address list, kind table) that fills a <see cref="ShiftManifest"/>. It sorts
/// the addresses ascending (a plain <c>uint</c> order, so the caller's order does not matter), runs F3 (<see cref="KindCounts"/>), lays
/// the units out kind-ordered (ascending kind index, n_k copies each), runs every draw through <see cref="ManifestDraws.Run"/> (exactly
/// <c>K = 2 nb + 2 N + max(0, N - 1)</c>: jitters, the shuffle, then per batch the street pairs, addresses and Cursed rolls), and prices
/// each unit with F1 (<see cref="SpawnValue"/>, the drawn address's district, spawn shift = the profile's shift). Batch ticks are
/// offsets from the Delivery-start tick. Every buffer is the manifest's own: 0 B, never throws.
/// </summary>
/// <example>
/// <code>
/// int kinds = ManifestRoller.BuildKinds(mix, content.MailKinds.AsSpan(), profile.Shift, acceptsMask, shares, deadlineOffsets);
/// int n = ManifestRoller.Roll(view.Create(in RngNames.Mail, (uint)profile.Shift), in profile, addresses, shares[..kinds], deadlineOffsets[..kinds], in batchKnobs, in valueKnobs, manifest);
/// </code>
/// </example>
public static class ManifestRoller
{
    /// <summary>
    /// The F3 kind table for <paramref name="shift"/>: per kind (by kind index) its base value, its share in the mix row for the shift
    /// (the row whose shift is the greatest at or below it; 0 for a kind the row does not list), its fallback kind and whether an unlocked
    /// destination accepts it (bit k of <paramref name="acceptsMask"/>), plus its deadline offset.
    /// </summary>
    /// <param name="mix">The mail mix.</param>
    /// <param name="kinds">The mail kinds, by kind index.</param>
    /// <param name="shift">The shift.</param>
    /// <param name="acceptsMask">The union of the unlocked destinations' accepted-kind masks.</param>
    /// <param name="shares">Receives one entry per kind.</param>
    /// <param name="deadlineOffsets">Receives each kind's <c>deadlineOffsetShifts</c> (clamped to a byte).</param>
    /// <returns>The kind count, or <see cref="ManifestRollStatus.InvalidKinds"/> when there are more kinds than the outputs or <see cref="KindCounts.MaxKinds"/>.</returns>
    public static int BuildKinds(
        MailMixDef mix, ReadOnlySpan<MailKindDef> kinds, int shift, ulong acceptsMask, Span<KindShare> shares, Span<byte> deadlineOffsets)
    {
        ArgumentNullException.ThrowIfNull(mix);
        if (kinds.Length > KindCounts.MaxKinds || kinds.Length > shares.Length || kinds.Length > deadlineOffsets.Length)
        {
            return ManifestRollStatus.InvalidKinds;
        }

        int row = -1;
        for (int r = 0; r < mix.Shifts.Length; r++)
        {
            int s = mix.Shifts[r].Shift;
            if (s <= shift && (row < 0 || s > mix.Shifts[row].Shift))
            {
                row = r;
            }
        }

        for (int k = 0; k < kinds.Length; k++)
        {
            MailKindDef def = kinds[k];
            int share = 0;
            if (row >= 0)
            {
                foreach (MailShare m in mix.Shifts[row].Shares)
                {
                    share += m.Kind.Value == k ? m.SharePm : 0;
                }
            }

            int fallback = def.FallbackKind.IsNone ? -1 : def.FallbackKind.Value;
            bool accepted = k < 64 && ((acceptsMask >> k) & 1UL) != 0UL;
            shares[k] = new KindShare(def.BaseValueCents, share, fallback, accepted);
            deadlineOffsets[k] = (byte)Math.Clamp(def.DeadlineOffsetShifts, 0, byte.MaxValue);
        }

        return kinds.Length;
    }

    /// <summary>Rolls the manifest (see the type remarks).</summary>
    /// <param name="rng">The Mail-owned <c>mail</c> stream for the shift, fresh (re-seeded).</param>
    /// <param name="p">The profile.</param>
    /// <param name="addresses">The unlocked addresses, in any order (at most the manifest's address capacity).</param>
    /// <param name="shares">The F3 kind table (<see cref="BuildKinds"/>).</param>
    /// <param name="deadlineOffsets">Each kind's deadline offset (same length as <paramref name="shares"/>).</param>
    /// <param name="batchKnobs">The F2/F4 knobs.</param>
    /// <param name="valueKnobs">The F1/F3 knobs.</param>
    /// <param name="m">The manifest to fill (emptied first).</param>
    /// <returns>N, or a negative code (the manifest is then empty; <see cref="ShiftManifest.Status"/> holds it).</returns>
    public static int Roll(
        RngStream rng,
        in SpawnProfile p,
        ReadOnlySpan<AddressId> addresses,
        ReadOnlySpan<KindShare> shares,
        ReadOnlySpan<byte> deadlineOffsets,
        in BatchKnobs batchKnobs,
        in MailValueKnobs valueKnobs,
        ShiftManifest m)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(m);
        m.Reset(0);
        if (addresses.Length > m.SortedAddresses.Length)
        {
            return Fail(m, ManifestRollStatus.TooManyAddresses);
        }

        if (shares.Length > KindCounts.MaxKinds || deadlineOffsets.Length != shares.Length)
        {
            return Fail(m, ManifestRollStatus.InvalidKinds);
        }

        Span<AddressId> sorted = m.SortedAddresses.AsSpan(0, addresses.Length);
        Span<int> districts = m.Districts.AsSpan(0, addresses.Length);
        SortAscending(addresses, sorted, districts);

        Span<int> counts = stackalloc int[KindCounts.MaxKinds];
        MeanDist md = MeanDist.Create(districts, valueKnobs.DistanceStepPm);
        int n = KindCounts.Compute(p.SpawnBudgetCents, shares, in md, in valueKnobs, p.Shift, counts[..shares.Length]);
        if (n < 0)
        {
            return Fail(m, n);
        }

        if (n > ShiftManifest.MaxUnits)
        {
            return Fail(m, ManifestRollStatus.TooManyUnits);
        }

        int at = 0;
        for (int k = 0; k < shares.Length; k++)
        {
            for (int c = 0; c < counts[k]; c++)
            {
                m.UnitKinds[at++] = (byte)k;
            }
        }

        var plan = new BatchPlan(0, p.DeliverySeconds, p.RaidWindowSeconds, n);
        int nb = ManifestDraws.Run(
            rng, in plan, in batchKnobs, p.CursedSharePm, sorted, m.UnitKinds, m.BatchTicks, m.Counts, m.UnitAddresses, m.UnitCursed);
        if (nb < 0)
        {
            return Fail(m, nb);
        }

        Fill(m, in p, shares, deadlineOffsets, in valueKnobs, nb);
        m.Complete(n, nb);
        return n;
    }

    private static void Fill(
        ShiftManifest m, in SpawnProfile p, ReadOnlySpan<KindShare> shares, ReadOnlySpan<byte> deadlineOffsets, in MailValueKnobs k, int nb)
    {
        int unit = 0;
        for (int b = 0; b < nb; b++)
        {
            int count = m.Counts[b];
            m.SetBatch(b, m.BatchTicks[b], count, unit);
            for (int i = 0; i < count; i++, unit++)
            {
                int kind = m.UnitKinds[unit];
                AddressId address = m.UnitAddresses[unit];
                bool cursed = m.UnitCursed[unit];
                ushort value = SpawnValue.Compute((int)shares[kind].BaseValueCents, (int)address.District, p.Shift, cursed, found: false, in k);
                byte deadline = (byte)Math.Clamp(p.Shift + deadlineOffsets[kind], 0, byte.MaxValue);
                m.SetUnit(unit, DefIndex.FromValue((ushort)kind), address, cursed ? MailFlags.Cursed : MailFlags.None, value, deadline, b);
            }
        }
    }

    // Insertion sort into the scratch (the registry's list is already ascending, so this is one pass there); districts follow.
    private static void SortAscending(ReadOnlySpan<AddressId> source, Span<AddressId> sorted, Span<int> districts)
    {
        for (int i = 0; i < source.Length; i++)
        {
            AddressId a = source[i];
            int j = i - 1;
            while (j >= 0 && sorted[j].Value > a.Value)
            {
                sorted[j + 1] = sorted[j];
                j--;
            }

            sorted[j + 1] = a;
        }

        for (int i = 0; i < sorted.Length; i++)
        {
            districts[i] = (int)sorted[i].District;
        }
    }

    private static int Fail(ShiftManifest m, int status)
    {
        m.Reset(status);
        return status;
    }
}
