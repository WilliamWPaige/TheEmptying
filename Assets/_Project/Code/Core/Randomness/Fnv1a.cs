using System.Text;

namespace TheEmptying.Core.Randomness
{
    public static class Fnv1a
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Hash64(string value)
        {
            ulong hash = OffsetBasis;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= Prime;
            }

            return hash;
        }
    }
}
