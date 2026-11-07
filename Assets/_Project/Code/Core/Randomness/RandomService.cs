using System.Collections.Generic;

namespace TheEmptying.Core.Randomness
{
    /// <summary>
    /// Hands out named, seeded random streams. Every stream's seed is derived from
    /// the world seed plus a stable FNV-1a hash of the stream name, so adding rolls
    /// to one stream never shifts another's results.
    /// </summary>
    public sealed class RandomService
    {
        private readonly ulong _worldSeed;
        private readonly Dictionary<string, SimRandom> _streams = new Dictionary<string, SimRandom>();

        public RandomService(ulong worldSeed)
        {
            _worldSeed = worldSeed;
        }

        public ulong WorldSeed => _worldSeed;

        public SimRandom GetStream(string streamName)
        {
            if (!_streams.TryGetValue(streamName, out SimRandom stream))
            {
                ulong hash = Fnv1a.Hash64(streamName);
                ulong seed = CombineSeed(_worldSeed, hash);
                stream = SimRandom.CreateSeeded(seed, hash);
                _streams[streamName] = stream;
            }

            return stream;
        }

        public RandomServiceState ExportState()
        {
            var state = new RandomServiceState();
            foreach (KeyValuePair<string, SimRandom> entry in _streams)
            {
                SimRandomState streamState = entry.Value.GetState();
                state.Streams.Add(new RandomStreamEntry
                {
                    Name = entry.Key,
                    State = streamState.State,
                    Inc = streamState.Inc
                });
            }

            return state;
        }

        /// <summary>
        /// Restores every listed stream. A stream already handed out (and so
        /// possibly captured elsewhere, e.g. by a module's scheduled system) is
        /// mutated in place rather than replaced, so that capture stays valid.
        /// </summary>
        public void RestoreState(RandomServiceState state)
        {
            foreach (RandomStreamEntry entry in state.Streams)
            {
                var restored = new SimRandomState(entry.State, entry.Inc);
                if (_streams.TryGetValue(entry.Name, out SimRandom existing))
                {
                    existing.SetState(restored);
                }
                else
                {
                    _streams[entry.Name] = SimRandom.FromState(restored);
                }
            }
        }

        private static ulong CombineSeed(ulong worldSeed, ulong streamHash)
        {
            unchecked
            {
                return worldSeed ^ (streamHash + 0x9E3779B97F4A7C15UL + (worldSeed << 6) + (worldSeed >> 2));
            }
        }
    }
}
