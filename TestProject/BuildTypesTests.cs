using CrashDumpAnalyzer.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;

namespace CrashDumpAnalyzer.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class BuildTypesTests
    {
        private Mock<ILogger>? _loggerMock;
        private IConfiguration? _configuration;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger>();

            _configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings_example.json", optional: false)
                .Build();
        }

        [TestMethod]
        public void Initialize_ShouldSetupBuildTypesAndRegex()
        {
            Assert.IsNotNull(_loggerMock);
            Assert.IsNotNull(_configuration);
            BuildTypes.Initialize(_loggerMock.Object, _configuration);

            Assert.IsTrue(BuildTypes.HasRegex());
            Assert.AreEqual(5, BuildTypes.Types.Count);
            Assert.AreEqual(0, BuildTypes.Types["development"]);
            Assert.AreEqual(1, BuildTypes.Types["alpha"]);
            Assert.AreEqual(2, BuildTypes.Types["beta"]);
            Assert.AreEqual(3, BuildTypes.Types["rc"]);
            Assert.AreEqual(4, BuildTypes.Types[""]);
        }

        [TestMethod]
        public void ExtractBuildType_ShouldReturnCorrectBuildType()
        {
            Assert.IsNotNull(_loggerMock);
            Assert.IsNotNull(_configuration);
            BuildTypes.Initialize(_loggerMock.Object, _configuration);

            var buildType = BuildTypes.ExtractBuildType("        CompanyName:      xYz Company\r\n        ProductName:      xYz of the xYz company\r\n        InternalName:     xYz\r\n        OriginalFilename: xYz.exe\r\n        ProductVersion:   4.5.0 (6657-alpha)\r\n        FileVersion:      4.5.0 (6657-alpha)\r\n        PrivateBuild:     NOT RELEASED! For Testing Purposes Only\r\n        FileDescription:  xYz of the xYz company\r\n        LegalCopyright:   Copyright \u00a9 2024 xYz company\r\n        LegalTrademarks:  \r\n        Comments:         ");
            Assert.AreEqual(1, buildType);
        }

        [TestMethod]
        public void ExtractBuildType_ShouldReturnNullForUnknownBuildType()
        {
            Assert.IsNotNull(_loggerMock);
            Assert.IsNotNull(_configuration);
            BuildTypes.Initialize(_loggerMock.Object, _configuration);

            var buildType = BuildTypes.ExtractBuildType("version unknown");
            Assert.AreEqual(buildType, -1);
        }

        [TestMethod]
        public void BuildTypeStrings_ShouldReturnAllBuildTypeKeys()
        {
            Assert.IsNotNull(_loggerMock);
            Assert.IsNotNull(_configuration);
            BuildTypes.Initialize(_loggerMock.Object, _configuration);

            var buildTypeStrings = BuildTypes.BuildTypeStrings();
            CollectionAssert.AreEquivalent(new List<string> {"development", "alpha", "beta", "rc", "release" }, buildTypeStrings);
        }
    }
}
