namespace TheEmptying.Modules.Ponds
{
    /// <summary>
    /// R17: one pond - a roughly round patch of water, its own random
    /// size, and the one piece of state that makes it different from a
    /// tree, stone or grub mound: how many drinks it currently holds.
    /// </summary>
    public sealed class PondState
    {
        public double CenterX { get; set; }
        public double CenterZ { get; set; }
        public double RadiusMeters { get; set; }
        public int DrinksCurrent { get; set; }

        public bool IsDry => DrinksCurrent <= 0;
    }
}
