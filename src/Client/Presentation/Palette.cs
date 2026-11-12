using Godot;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>Flat colours shared by the world and the UI, so a house, its mailbox and its mail read as one colour.</summary>
    public static class Palette
    {
        private static readonly Color[] House =
        {
            new Color("d1495b"), new Color("edae49"), new Color("66a182"), new Color("2e86ab"), new Color("8d6a9f"),
            new Color("f79256"), new Color("00798c"), new Color("c5d86d"), new Color("b56576"), new Color("5e6472"),
        };

        private static readonly Color[] Player =
        {
            new Color("f4d35e"), new Color("ee964b"), new Color("5bc0eb"), new Color("9bc53d"),
            new Color("e55934"), new Color("c3a6ff"), new Color("fa7921"), new Color("7fd1b9"),
        };

        private static readonly Color[] Mail =
        {
            new Color("f3e9d2"), new Color("fff8e7"), new Color("c9a66b"), new Color("a47148"), new Color("7f5539"), new Color("596475"),
        };

        public static readonly Color Grass = new Color("6a994e");
        public static readonly Color Street = new Color("4a4e55");
        public static readonly Color Lot = new Color("a7c957");
        public static readonly Color Yard = new Color("b8b0a0");
        public static readonly Color PostOffice = new Color("9c4a3c");
        public static readonly Color Intake = new Color("2f6fde");
        public static readonly Color Match = new Color("7ae582");
        public static readonly Color Mismatch = new Color("ff6b6b");
        public static readonly Color Amber = new Color("ffbf47");

        public static Color HouseColour(int variant) => House[Mod(variant, House.Length)];
        public static Color PlayerColour(int slot) => Player[Mod(slot, Player.Length)];
        public static Color MailColour(int kindIndex) => Mail[Mod(kindIndex, Mail.Length)];

        public static Color Tile(TileType type) => type switch
        {
            TileType.Street => Street,
            TileType.Lot => Lot,
            TileType.Building => Lot,
            TileType.Yard => Yard,
            _ => Grass,
        };

        public static StandardMaterial3D Flat(Color colour) => new StandardMaterial3D { AlbedoColor = colour, Roughness = 1f };

        private static int Mod(int v, int n) => ((v % n) + n) % n;
    }
}
