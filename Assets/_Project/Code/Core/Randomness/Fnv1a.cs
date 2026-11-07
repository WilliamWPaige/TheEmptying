using System.Text;

namespace TheEmptying.Core.Randomness
{
    public static class Fnv1a
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong InitialHash => OffsetBasis;

        public static ulong Hash64(string value)
        {
            return Hash64(Encoding.UTF8.GetBytes(value));
        }

        public static ulong Hash64(byte[] bytes)
        {
            return Continue(OffsetBasis, bytes);
        }

        /// <summary>Feeds more bytes into an in-progress hash, so several byte arrays can be combined into one hash.</summary>
        public static ulong Continue(ulong hash, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= Prime;
            }

            return hash;
        }
    }
}
