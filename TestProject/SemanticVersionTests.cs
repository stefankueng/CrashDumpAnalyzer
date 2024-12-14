using CrashDumpAnalyzer.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CrashDumpAnalyzer.Tests
{
    [TestClass]
    public class SemanticVersionTests
    {
        [TestMethod]
        public void Constructor_WithIntegerParameters_ShouldInitializeCorrectly()
        {
            var version = new SemanticVersion(1, 2, 3, 4, 0);
            Assert.AreEqual("1.2.3.4", version.ToString());
        }

        [TestMethod]
        public void Constructor_WithVersionString_ShouldInitializeCorrectly()
        {
            var version = new SemanticVersion("1.2.3.4", 0);
            Assert.AreEqual("1.2.3.4", version.ToString());
        }

        [TestMethod]
        public void Equals_ShouldReturnTrueForEqualVersions()
        {
            var version1 = new SemanticVersion(1, 2, 3, 4, 0);
            var version2 = new SemanticVersion("1.2.3.4", 0);
            Assert.IsTrue(version1.Equals(version2));
        }

        [TestMethod]
        public void Equals_ShouldReturnFalseForDifferentVersions()
        {
            var version1 = new SemanticVersion(1, 2, 3, 4, 0);
            var version2 = new SemanticVersion("1.2.3.5", 0);
            Assert.IsFalse(version1.Equals(version2));
            var version3 = new SemanticVersion(1, 2, 3, 4, 1);
            Assert.IsFalse(version1.Equals(version3));
        }

        [TestMethod]
        public void GetHashCode_ShouldReturnSameHashCodeForEqualVersions()
        {
            var version1 = new SemanticVersion(1, 2, 3, 4, 1);
            var version2 = new SemanticVersion("1.2.3.4", 1);
            Assert.AreEqual(version1.GetHashCode(), version2.GetHashCode());
        }

        [TestMethod]
        public void ToString_ShouldReturnCorrectVersionString()
        {
            var version = new SemanticVersion(1, 2, 3, 4, 1);
            Assert.AreEqual("1.2.3.4", version.ToString());
        }

        [TestMethod]
        public void ToVersionString_WithPrefix_ShouldReturnCorrectVersionString()
        {
            var version = new SemanticVersion(1, 2, 3, 4, 1);
            Assert.AreEqual("v1.2.3.4", version.ToVersionString("v"));
        }

        [TestMethod]
        public void ComparisonOperators_ShouldWorkCorrectly()
        {
            var version1 = new SemanticVersion(1, 2, 3, 4, 1);
            var version2 = new SemanticVersion(1, 2, 3, 4, 2);
            var version3 = new SemanticVersion(1, 2, 3, 5, 1);
            var version4 = new SemanticVersion(1, 2, 3, 20, 0);

            Assert.IsTrue(version1 < version2);
            Assert.IsTrue(version2 > version1);
            Assert.IsTrue(version1 <= version2);
            Assert.IsTrue(version2 >= version1);
            Assert.IsTrue(version1 != version2);
            Assert.IsTrue(version1 < version3);
            Assert.IsTrue(version3 > version1);
            Assert.IsTrue(version1 > version4);
        }
    }
}
