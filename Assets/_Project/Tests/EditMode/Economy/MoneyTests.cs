using NUnit.Framework;
using TheEmptying.Core.Economy;

namespace TheEmptying.Tests.EditMode.Economy
{
    public class MoneyTests
    {
        [Test]
        public void FromCopper_StoresWholeHundredths()
        {
            Assert.AreEqual(250, Money.FromCopper(2.5).HundredthsOfCopper);
            Assert.AreEqual(2000, Money.FromCopper(20L).HundredthsOfCopper);
        }

        [Test]
        public void Breakdown_SplitsIntoGoldSilverCopperAndRemainder()
        {
            // 1 gold (10000) + 2 silver (2000) + 3 copper (300) + 45 hundredths = 12345
            Money money = Money.FromHundredthsOfCopper(12345);

            Assert.AreEqual(1, money.Gold);
            Assert.AreEqual(2, money.Silver);
            Assert.AreEqual(3, money.WholeCopper);
            Assert.AreEqual(45, money.RemainderHundredths);
        }

        [Test]
        public void ToString_WholeAmount_OmitsZeroDenominations()
        {
            Assert.AreEqual("2 silver 3 copper", Money.FromHundredthsOfCopper(2300).ToString());
            Assert.AreEqual("1 gold", Money.FromHundredthsOfCopper(10000).ToString());
            Assert.AreEqual("0 copper", Money.Zero.ToString());
        }

        [Test]
        public void ToString_WithRemainder_ShowsFractionalCopper()
        {
            Assert.AreEqual("3.50 copper", Money.FromHundredthsOfCopper(350).ToString());
        }

        [Test]
        public void Arithmetic_AddSubtractAndCompare()
        {
            Money a = Money.FromCopper(10);
            Money b = Money.FromCopper(4);

            Assert.AreEqual(Money.FromCopper(14), a + b);
            Assert.AreEqual(Money.FromCopper(6), a - b);
            Assert.IsTrue(a > b);
            Assert.IsTrue(b < a);
            Assert.IsTrue(Money.FromCopper(10) >= a);
        }

        [Test]
        public void ApplyPercent_FivePercentOfATwoCopperPrice_IsNotLostToRounding()
        {
            // The bug this fixes: 5% of 2 copper is 0.1 copper (10 hundredths), which
            // rounded to nothing in half-copper units. In hundredths it's exact.
            Money price = Money.FromCopper(2);
            Money changed = price.ApplyPercent(5.0);

            Assert.AreEqual(210, changed.HundredthsOfCopper);
            Assert.AreNotEqual(price, changed);
        }

        [Test]
        public void ApplyPercent_NegativePercent_DecreasesTheAmount()
        {
            Money price = Money.FromCopper(20);
            Money changed = price.ApplyPercent(-5.0);

            Assert.AreEqual(1900, changed.HundredthsOfCopper);
        }

        [Test]
        public void Equality_SameHundredths_AreEqual()
        {
            Assert.AreEqual(Money.FromCopper(2.5), Money.FromHundredthsOfCopper(250));
            Assert.IsTrue(Money.FromCopper(1) == Money.FromHundredthsOfCopper(100));
            Assert.IsTrue(Money.FromCopper(1) != Money.FromHundredthsOfCopper(99));
        }
    }
}
