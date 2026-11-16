using NUnit.Framework;
using TheEmptying.Core.Economy;

namespace TheEmptying.Tests.EditMode.Economy
{
    public class MoneyTests
    {
        [Test]
        public void FromShil_StoresWholeHundredths()
        {
            Assert.AreEqual(250, Money.FromShil(2.5).HundredthsOfShil);
            Assert.AreEqual(2000, Money.FromShil(20L).HundredthsOfShil);
        }

        [Test]
        public void Breakdown_SplitsIntoJemmGildShilAndRemainder()
        {
            // 1 jemm (10000) + 2 gild (2000) + 3 shil (300) + 45 hundredths = 12345
            Money money = Money.FromHundredthsOfShil(12345);

            Assert.AreEqual(1, money.Jemm);
            Assert.AreEqual(2, money.Gild);
            Assert.AreEqual(3, money.WholeShil);
            Assert.AreEqual(45, money.RemainderHundredths);
        }

        [Test]
        public void ToString_WholeAmount_OmitsZeroDenominations()
        {
            Assert.AreEqual("2 gild 3 shil", Money.FromHundredthsOfShil(2300).ToString());
            Assert.AreEqual("1 jemm", Money.FromHundredthsOfShil(10000).ToString());
            Assert.AreEqual("0 shil", Money.Zero.ToString());
        }

        [Test]
        public void ToString_WithRemainder_ShowsFractionalShil()
        {
            Assert.AreEqual("3.50 shil", Money.FromHundredthsOfShil(350).ToString());
        }

        [Test]
        public void Arithmetic_AddSubtractAndCompare()
        {
            Money a = Money.FromShil(10);
            Money b = Money.FromShil(4);

            Assert.AreEqual(Money.FromShil(14), a + b);
            Assert.AreEqual(Money.FromShil(6), a - b);
            Assert.IsTrue(a > b);
            Assert.IsTrue(b < a);
            Assert.IsTrue(Money.FromShil(10) >= a);
        }

        [Test]
        public void ApplyPercent_FivePercentOfATwoShilPrice_IsNotLostToRounding()
        {
            // The bug this fixes: 5% of 2 shil is 0.1 shil (10 hundredths), which
            // rounded to nothing in half-shil units. In hundredths it's exact.
            Money price = Money.FromShil(2);
            Money changed = price.ApplyPercent(5.0);

            Assert.AreEqual(210, changed.HundredthsOfShil);
            Assert.AreNotEqual(price, changed);
        }

        [Test]
        public void ApplyPercent_NegativePercent_DecreasesTheAmount()
        {
            Money price = Money.FromShil(20);
            Money changed = price.ApplyPercent(-5.0);

            Assert.AreEqual(1900, changed.HundredthsOfShil);
        }

        [Test]
        public void Equality_SameHundredths_AreEqual()
        {
            Assert.AreEqual(Money.FromShil(2.5), Money.FromHundredthsOfShil(250));
            Assert.IsTrue(Money.FromShil(1) == Money.FromHundredthsOfShil(100));
            Assert.IsTrue(Money.FromShil(1) != Money.FromHundredthsOfShil(99));
        }
    }
}
