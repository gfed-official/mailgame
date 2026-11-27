using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Tools.SeedViewer
{
    /// <summary>
    /// The SeedViewer of doc 12 M1: what the generator makes of a seed, without starting the game.
    ///   SeedViewer [SEED[-LAST]] [--large] [--map] [--island] [--content=DIR]
    /// (--large: the Large Island of the seed rather than the Small Island)
    /// One line of figures per seed (size, houses, districts and their houses, streets, nodes,
    /// the world hash) and with --map the town drawn in letters, a character a tile:
    /// '@' a raiders' way in, '=' street, '#' house, '.' lot, 'P' Post Office, 'y' its yard, '~' beach, ',' shallow and ';' deep water, '-' a ferry lane, 'T' tree,
    /// 'o' boulder, '*' bush, 's' sand pile, ' ' grass; the digit of a house's district on its
    /// mailbox. With --island the whole island, a character for 4 × 4 tiles, by its biome:
    /// '#' deep water, '~' shallow, ':' beach, '.' grassland, 'T' forest, '^' rocky, 'H' the town.
    /// Exit code 0; 1 if a seed makes no town; 2 for a wrong command.
    /// </summary>
    public static class Program
    {
        public const int ExitOk = 0, ExitInvalid = 1, ExitUsage = 2;

        public static int Main(string[] args) => Run(args, Directory.GetCurrentDirectory(), Console.Out, Console.Error);

        public static int Run(string[] args, string workingDir, TextWriter output, TextWriter errors)
        {
            uint first = 1, last = 1;
            bool drawMap = false, drawIsland = false, large = false;
            string? contentDir = null;
            foreach (string arg in args)
            {
                if (arg == "--map") drawMap = true;
                else if (arg == "--large") large = true;
                else if (arg == "--island") drawIsland = true;
                else if (arg.StartsWith("--content=", StringComparison.Ordinal)) contentDir = arg.Substring(10);
                else if (arg == "--help" || arg == "-h")
                {
                    output.WriteLine("usage: SeedViewer [SEED[-LAST]] [--large] [--map] [--island] [--content=DIR]");
                    return ExitOk;
                }
                else if (!arg.StartsWith('-') && TryRange(arg, out first, out last)) { }
                else
                {
                    errors.WriteLine($"SeedViewer: unexpected argument '{arg}' (usage: SeedViewer [SEED[-LAST]] [--large] [--map] [--island] [--content=DIR])");
                    return ExitUsage;
                }
            }

            ContentDb content;
            try
            {
                content = contentDir != null
                    ? ContentDb.Load(new DirectoryContentSource(Path.GetFullPath(contentDir, workingDir)))
                    : ContentDb.LoadFromDirectoryTree(workingDir);
            }
            catch (ContentException e)
            {
                errors.WriteLine($"SeedViewer: {e.Message}");
                return ExitInvalid;
            }

            int status = ExitOk;
            for (uint seed = first; ; seed++)
            {
                WorldMap map;
                try { map = large ? LargeIslandGenerator.Generate(content, seed) : WorldGenerator.Generate(content, seed); }
                catch (InvalidOperationException e)
                {
                    errors.WriteLine($"seed {seed}: {e.Message}");
                    status = ExitInvalid;
                    if (seed == last) break;
                    continue;
                }
                output.WriteLine(Summary(map, seed));
                if (drawMap) output.Write(Draw(map));
                if (drawIsland && map.Terrain != null) output.Write(DrawIsland(map.Terrain));
                if (seed == last) break;
            }
            return status;
        }

        private static bool TryRange(string arg, out uint first, out uint last)
        {
            int dash = arg.IndexOf('-');
            if (dash < 0)
            {
                bool ok = uint.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out first);
                last = first;
                return ok;
            }
            last = 0;
            return uint.TryParse(arg.Substring(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out first)
                && uint.TryParse(arg.Substring(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out last) && last >= first;
        }

        public static string Summary(WorldMap map, uint seed)
        {
            var districts = string.Join(" ", map.Districts.Select(d => map.Destinations.Count(h => h.Address.District == d.Id).ToString(CultureInfo.InvariantCulture)));
            int nodes = map.Props.Count(p => p.Kind == PropKind.Tree || p.Kind == PropKind.Bush || p.Kind == PropKind.Boulder || p.Kind == PropKind.SandPile);
            int names = map.Streets.Select(s => s.Name).Distinct().Count();
            int Of(PropKind kind) => map.Props.Count(p => p.Kind == kind);
            string island = "";
            if (map.Terrain is IslandTerrain t)
            {
                int land = t.LandTiles;
                string Share(Biome b) => (100.0 * t.Count(b) / Math.Max(1, land)).ToString("F0", CultureInfo.InvariantCulture);
                island = string.Create(CultureInfo.InvariantCulture,
                    $", island {t.Cols}x{t.Rows} land {100.0 * land / t.Biomes.Length:F0}% (forest {Share(Biome.Forest)}% rocky {Share(Biome.Rocky)}% beach {Share(Biome.Beach)}%), top {t.MaxHeightCm / 100.0:F1} m, town at {t.TownX},{t.TownZ} (flat {t.Site.Flat / 10}% levelled {t.Site.Levelling} cm, {t.Site.Coast * 2} m from the sea, {t.Site.FromCentre * 2} m off the middle)");
            }
            string edges = string.Join(" ", map.SpawnEdges.Select(e => $"{e.Direction}@{e.UnlockShift}"));
            int docks = map.Destinations.Count(d => d.IsDock);
            string ferries = map.FerryLanes.Count == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $", ferry lanes {string.Join(" ", map.FerryLanes.Select(l => $"{l.Length:F0} m ({l.AX:F0},{l.AZ:F0} to {l.BX:F0},{l.BZ:F0})"))}");
            return string.Create(CultureInfo.InvariantCulture,
                $"seed {seed}: {map.Cols}x{map.Rows} tiles, {map.Destinations.Count - docks} houses, {docks} docks, {map.Districts.Count} districts ({districts}), {names} streets, {nodes} nodes (trees {Of(PropKind.Tree)}, boulders {Of(PropKind.Boulder)}, bushes {Of(PropKind.Bush)}, sand {Of(PropKind.SandPile)}), routes {map.Routes.Nodes.Count} nodes {map.Routes.Edges.Count} edges {map.Routes.TotalLength:F0} m{ferries}, raiders from {edges}{island}, worldHash {map.MapHash:x16}");
        }

        public static string Draw(WorldMap map)
        {
            var grid = new char[map.Rows, map.Cols];
            for (int z = 0; z < map.Rows; z++)
                for (int x = 0; x < map.Cols; x++)
                    grid[z, x] = map.GetTile(x, z) switch
                    {
                        TileType.Street => '=',
                        TileType.Building => '#',
                        TileType.Lot => '.',
                        TileType.Yard => 'y',
                        TileType.Beach => '~',
                        TileType.Water => ',',
                        TileType.DeepWater => ';',
                        _ => ' ',
                    };
            foreach (var solid in map.PostOffice.Solid)
                for (int z = solid.Y; z < solid.Y + solid.H; z++)
                    for (int x = solid.X; x < solid.X + solid.W; x++) grid[z, x] = 'P';
            foreach (var p in map.Props)
            {
                int x = (int)(p.X / 2f), z = (int)(p.Z / 2f);
                if (!map.InBounds(x, z) || grid[z, x] != ' ' && grid[z, x] != '~') continue;
                char c = p.Kind switch { PropKind.Tree => 'T', PropKind.Boulder => 'o', PropKind.Bush => '*', PropKind.SandPile => 's', _ => grid[z, x] };
                grid[z, x] = c;
            }
            foreach (var d in map.Destinations)
            {
                int x = (int)(d.MailboxX / 2f), z = (int)(d.MailboxZ / 2f);
                if (map.InBounds(x, z)) grid[z, x] = (char)('0' + d.Address.District % 10);
            }
            // Ferry lanes: '-' over the water.
            foreach (var lane in map.FerryLanes)
                for (int k = 0; k <= 40; k++)
                {
                    int x = (int)((lane.AX + (lane.BX - lane.AX) * k / 40f) / 2f), z = (int)((lane.AZ + (lane.BZ - lane.AZ) * k / 40f) / 2f);
                    if (map.InBounds(x, z) && map.IsWater(x, z)) grid[z, x] = '-';
                }
            // The raiders' ways in: '@'.
            foreach (var e in map.SpawnEdges)
            {
                int x = Math.Clamp((int)(e.X / 2f), 0, map.Cols - 1), z = Math.Clamp((int)(e.Z / 2f), 0, map.Rows - 1);
                grid[z, x] = '@';
            }
            var sb = new StringBuilder();
            for (int z = 0; z < map.Rows; z++)
            {
                for (int x = 0; x < map.Cols; x++) sb.Append(grid[z, x]);
                sb.Append('\n');
            }
            return sb.ToString();
        }
    
        public static string DrawIsland(IslandTerrain t)
        {
            const int step = 4;
            var sb = new StringBuilder();
            for (int z = 0; z < t.Rows; z += step)
            {
                for (int x = 0; x < t.Cols; x += step)
                {
                    int cx = x + step / 2, cz = z + step / 2;
                    bool town = cx >= t.TownX && cz >= t.TownZ && cx < t.TownX + t.TownCols && cz < t.TownZ + t.TownRows;
                    sb.Append(town ? 'H' : t.BiomeAt(cx, cz) switch
                    {
                        Biome.DeepWater => '#',
                        Biome.ShallowWater => '~',
                        Biome.Beach => ':',
                        Biome.Forest => 'T',
                        Biome.Rocky => '^',
                        _ => '.',
                    });
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
}
}
