using System;
using System.Collections.Generic;

namespace TheEmptying.Core.Randomness
{
    /// <summary>
    /// A PCG32 generator. A reference type (not a struct) so a single instance's
    /// state mutates in place wherever it is held, instead of being silently
    /// copied by value.
    /// </summary>
    public sealed class SimRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _inc;

        private SimRandom(ulong state, ulong inc)
        {
            _state = state;
            _inc = inc;
        }

        public static SimRandom CreateSeeded(ulong seed, ulong sequence)
        {
            var rng = new SimRandom(0, (sequence << 1) | 1);
            rng.NextUInt();
            rng._state += seed;
            rng.NextUInt();
            return rng;
        }

        public static SimRandom FromState(SimRandomState state)
        {
            return new SimRandom(state.State, state.Inc);
        }

        /// <summary>Mutates this instance in place, so anything already holding a reference to it sees the restored sequence.</summary>
        public void SetState(SimRandomState state)
        {
            _state = state.State;
            _inc = state.Inc;
        }

        public SimRandomState GetState()
        {
            return new SimRandomState(_state, _inc);
        }

        public uint NextUInt()
        {
            ulong oldState = _state;
            _state = unchecked(oldState * Multiplier + _inc);
            uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rot = (int)(oldState >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
            }

            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)NextBoundedUInt(range);
        }

        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1.0f / (1 << 24));
        }

        public bool Chance(float probability)
        {
            float roll = NextFloat();

            if (probability <= 0f)
            {
                return false;
            }

            if (probability >= 1f)
            {
                return true;
            }

            return roll < probability;
        }

        public int WeightedPick(IReadOnlyList<float> weights)
        {
            if (weights == null || weights.Count == 0)
            {
                throw new ArgumentException("weights must be a non-empty list.", nameof(weights));
            }

            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                total += weights[i];
            }

            if (total <= 0f)
            {
                throw new ArgumentException("weights must sum to more than zero.", nameof(weights));
            }

            float roll = NextFloat() * total;
            float cumulative = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative)
                {
                    return i;
                }
            }

            return weights.Count - 1;
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private uint NextBoundedUInt(uint bound)
        {
            if (bound == 0)
            {
                return 0;
            }

            uint threshold = unchecked((0u - bound) % bound);
            while (true)
            {
                uint candidate = NextUInt();
                if (candidate >= threshold)
                {
                    return candidate % bound;
                }
            }
        }
    }
}
