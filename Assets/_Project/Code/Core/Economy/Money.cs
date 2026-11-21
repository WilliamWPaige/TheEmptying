using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace TheEmptying.Core.Economy
{
    /// <summary>
    /// An amount of money, stored as a 64-bit whole number of hundredths of a
    /// copper so every amount - prices, accounts, wages, taxes - stays exact,
    /// even after a 5% market swing on a 2-copper loaf. 100 hundredths make a
    /// copper, 10 copper make a silver, 10 silver make a gold.
    /// </summary>
    public readonly struct Money : IEquatable<Money>, IComparable<Money>
    {
        private const long HundredthsPerCopper = 100;
        private const long CopperPerSilver = 10;
        private const long SilverPerGold = 10;
        private const long HundredthsPerSilver = HundredthsPerCopper * CopperPerSilver;
        private const long HundredthsPerGold = HundredthsPerSilver * SilverPerGold;

        [JsonProperty("hundredthsOfCopper")]
        public long HundredthsOfCopper { get; }

        [JsonConstructor]
        public Money(long hundredthsOfCopper)
        {
            HundredthsOfCopper = hundredthsOfCopper;
        }

        public static Money Zero => new Money(0);
        public static Money FromCopper(long copper) => new Money(copper * HundredthsPerCopper);
        public static Money FromHundredthsOfCopper(long hundredthsOfCopper) => new Money(hundredthsOfCopper);

        /// <summary>Rounds a fractional copper amount (for example 2.5) to the nearest hundredth.</summary>
        public static Money FromCopper(double copper) => new Money((long)Math.Round(copper * HundredthsPerCopper, MidpointRounding.AwayFromZero));

        public long Gold => HundredthsOfCopper / HundredthsPerGold;
        public long Silver => (HundredthsOfCopper % HundredthsPerGold) / HundredthsPerSilver;
        public long WholeCopper => (HundredthsOfCopper % HundredthsPerSilver) / HundredthsPerCopper;
        public long RemainderHundredths => HundredthsOfCopper % HundredthsPerCopper;

        /// <summary>Applies a percentage change (5.0 means +5%), rounded to the nearest hundredth of a copper.</summary>
        public Money ApplyPercent(double percent)
        {
            double scaled = HundredthsOfCopper * (1.0 + percent / 100.0);
            return new Money((long)Math.Round(scaled, MidpointRounding.AwayFromZero));
        }

        public static Money operator +(Money a, Money b) => new Money(a.HundredthsOfCopper + b.HundredthsOfCopper);
        public static Money operator -(Money a, Money b) => new Money(a.HundredthsOfCopper - b.HundredthsOfCopper);
        public static Money operator *(Money a, long factor) => new Money(a.HundredthsOfCopper * factor);
        public static Money operator *(long factor, Money a) => new Money(a.HundredthsOfCopper * factor);

        public static bool operator ==(Money a, Money b) => a.HundredthsOfCopper == b.HundredthsOfCopper;
        public static bool operator !=(Money a, Money b) => a.HundredthsOfCopper != b.HundredthsOfCopper;
        public static bool operator <(Money a, Money b) => a.HundredthsOfCopper < b.HundredthsOfCopper;
        public static bool operator >(Money a, Money b) => a.HundredthsOfCopper > b.HundredthsOfCopper;
        public static bool operator <=(Money a, Money b) => a.HundredthsOfCopper <= b.HundredthsOfCopper;
        public static bool operator >=(Money a, Money b) => a.HundredthsOfCopper >= b.HundredthsOfCopper;

        public bool Equals(Money other) => HundredthsOfCopper == other.HundredthsOfCopper;
        public override bool Equals(object obj) => obj is Money other && Equals(other);
        public override int GetHashCode() => HundredthsOfCopper.GetHashCode();
        public int CompareTo(Money other) => HundredthsOfCopper.CompareTo(other.HundredthsOfCopper);

        /// <summary>For example "2 silver 3 copper", or "3.50 copper" when there's a sub-copper remainder. "0 copper" for zero.</summary>
        public override string ToString()
        {
            var parts = new List<string>();
            long gold = Gold;
            long silver = Silver;
            long copper = WholeCopper;
            long remainder = RemainderHundredths;

            if (gold != 0)
            {
                parts.Add($"{gold} gold");
            }

            if (silver != 0)
            {
                parts.Add($"{silver} silver");
            }

            if (copper != 0 || remainder != 0 || parts.Count == 0)
            {
                parts.Add(remainder == 0
                    ? $"{copper} copper"
                    : $"{copper}.{remainder.ToString("D2", CultureInfo.InvariantCulture)} copper");
            }

            return string.Join(" ", parts);
        }
    }
}
