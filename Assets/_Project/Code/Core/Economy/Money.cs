using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace TheEmptying.Core.Economy
{
    /// <summary>
    /// An amount of money, stored as a 64-bit whole number of hundredths of a
    /// shil so every amount - prices, accounts, wages, taxes - stays exact,
    /// even after a 5% market swing on a 2-shil loaf. 100 hundredths make a
    /// shil, 10 shil make a gild, 10 gild make a jemm.
    /// </summary>
    public readonly struct Money : IEquatable<Money>, IComparable<Money>
    {
        private const long HundredthsPerShil = 100;
        private const long ShilPerGild = 10;
        private const long GildPerJemm = 10;
        private const long HundredthsPerGild = HundredthsPerShil * ShilPerGild;
        private const long HundredthsPerJemm = HundredthsPerGild * GildPerJemm;

        [JsonProperty("hundredthsOfShil")]
        public long HundredthsOfShil { get; }

        [JsonConstructor]
        public Money(long hundredthsOfShil)
        {
            HundredthsOfShil = hundredthsOfShil;
        }

        public static Money Zero => new Money(0);
        public static Money FromShil(long shil) => new Money(shil * HundredthsPerShil);
        public static Money FromHundredthsOfShil(long hundredthsOfShil) => new Money(hundredthsOfShil);

        /// <summary>Rounds a fractional shil amount (for example 2.5) to the nearest hundredth.</summary>
        public static Money FromShil(double shil) => new Money((long)Math.Round(shil * HundredthsPerShil, MidpointRounding.AwayFromZero));

        public long Jemm => HundredthsOfShil / HundredthsPerJemm;
        public long Gild => (HundredthsOfShil % HundredthsPerJemm) / HundredthsPerGild;
        public long WholeShil => (HundredthsOfShil % HundredthsPerGild) / HundredthsPerShil;
        public long RemainderHundredths => HundredthsOfShil % HundredthsPerShil;

        /// <summary>Applies a percentage change (5.0 means +5%), rounded to the nearest hundredth of a shil.</summary>
        public Money ApplyPercent(double percent)
        {
            double scaled = HundredthsOfShil * (1.0 + percent / 100.0);
            return new Money((long)Math.Round(scaled, MidpointRounding.AwayFromZero));
        }

        public static Money operator +(Money a, Money b) => new Money(a.HundredthsOfShil + b.HundredthsOfShil);
        public static Money operator -(Money a, Money b) => new Money(a.HundredthsOfShil - b.HundredthsOfShil);
        public static Money operator *(Money a, long factor) => new Money(a.HundredthsOfShil * factor);
        public static Money operator *(long factor, Money a) => new Money(a.HundredthsOfShil * factor);

        public static bool operator ==(Money a, Money b) => a.HundredthsOfShil == b.HundredthsOfShil;
        public static bool operator !=(Money a, Money b) => a.HundredthsOfShil != b.HundredthsOfShil;
        public static bool operator <(Money a, Money b) => a.HundredthsOfShil < b.HundredthsOfShil;
        public static bool operator >(Money a, Money b) => a.HundredthsOfShil > b.HundredthsOfShil;
        public static bool operator <=(Money a, Money b) => a.HundredthsOfShil <= b.HundredthsOfShil;
        public static bool operator >=(Money a, Money b) => a.HundredthsOfShil >= b.HundredthsOfShil;

        public bool Equals(Money other) => HundredthsOfShil == other.HundredthsOfShil;
        public override bool Equals(object obj) => obj is Money other && Equals(other);
        public override int GetHashCode() => HundredthsOfShil.GetHashCode();
        public int CompareTo(Money other) => HundredthsOfShil.CompareTo(other.HundredthsOfShil);

        /// <summary>For example "2 gild 3 shil", or "3.50 shil" when there's a sub-shil remainder. "0 shil" for zero.</summary>
        public override string ToString()
        {
            var parts = new List<string>();
            long jemm = Jemm;
            long gild = Gild;
            long shil = WholeShil;
            long remainder = RemainderHundredths;

            if (jemm != 0)
            {
                parts.Add($"{jemm} jemm");
            }

            if (gild != 0)
            {
                parts.Add($"{gild} gild");
            }

            if (shil != 0 || remainder != 0 || parts.Count == 0)
            {
                parts.Add(remainder == 0
                    ? $"{shil} shil"
                    : $"{shil}.{remainder.ToString("D2", CultureInfo.InvariantCulture)} shil");
            }

            return string.Join(" ", parts);
        }
    }
}
