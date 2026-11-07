using NUnit.Framework;
using TheEmptying.Core;

namespace TheEmptying.Tests.EditMode
{
    public class ProjectInfoTests
    {
        [Test]
        public void Version_IsExpected()
        {
            Assert.AreEqual("0.0.1", ProjectInfo.Version);
        }
    }
}
