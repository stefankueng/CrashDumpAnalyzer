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
    public class MinVersionsTests
    {
        private Mock<ILogger>? _loggerMock;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [TestMethod]
        public void IsVersionSupported_WhenApplicationNotConfigured_ShouldReturnTrue()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:SomeApp:1", "1.0.0.0" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);
            var version = new SemanticVersion("2.5.0.0", 0);

            // Act
            var result = MinVersions.IsVersionSupported("UnknownApp", version);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsVersionSupported_WhenMajorVersionNotConfigured_ShouldReturnTrue()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:TestApp:1", "1.5.0.0" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);
            var version = new SemanticVersion("3.0.0.0", 0); // Major version 3 not configured

            // Act
            var result = MinVersions.IsVersionSupported("TestApp", version);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsVersionSupported_WhenVersionBelowMinimum_ShouldReturnFalse()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:TestApp:1", "1.5.0.0" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);
            var version = new SemanticVersion("1.3.0.0", 0); // Below 1.5.0.0

            // Act
            var result = MinVersions.IsVersionSupported("TestApp", version);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsVersionSupported_WhenVersionEqualToMinimum_ShouldReturnTrue()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:TestApp:1", "1.5.0.0" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);
            var version = new SemanticVersion("1.5.0.0", 0); // Equal to 1.5.0.0

            // Act
            var result = MinVersions.IsVersionSupported("TestApp", version);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsVersionSupported_WhenVersionAboveMinimum_ShouldReturnTrue()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:TestApp:1", "1.5.0.0" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);
            var version = new SemanticVersion("1.6.0.0", 0); // Above 1.5.0.0

            // Act
            var result = MinVersions.IsVersionSupported("TestApp", version);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsVersionSupported_WithRegexApplicationPattern_ShouldMatchCorrectly()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:TestApp.*:1", "1.5.0.0" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);
            var version = new SemanticVersion("1.3.0.0", 0);

            // Act
            var resultMatch = MinVersions.IsVersionSupported("TestAppPro", version);
            var resultNoMatch = MinVersions.IsVersionSupported("OtherApp", version);

            // Assert
            Assert.IsFalse(resultMatch); // Matches regex, version too low
            Assert.IsTrue(resultNoMatch); // Doesn't match regex, not configured
        }

        [TestMethod]
        public void IsVersionSupported_WithMultipleMajorVersions_ShouldCheckCorrectMajor()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:TestApp:1", "1.5.0.0" },
                    { "MinVersions:TestApp:2", "2.3.0.0" },
                    { "MinVersions:TestApp:3", "3.1.0.0" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);

            // Act
            var v1Below = MinVersions.IsVersionSupported("TestApp", new SemanticVersion("1.4.0.0", 0));
            var v1Equal = MinVersions.IsVersionSupported("TestApp", new SemanticVersion("1.5.0.0", 0));
            var v2Below = MinVersions.IsVersionSupported("TestApp", new SemanticVersion("2.2.0.0", 0));
            var v2Above = MinVersions.IsVersionSupported("TestApp", new SemanticVersion("2.5.0.0", 0));
            var v3Below = MinVersions.IsVersionSupported("TestApp", new SemanticVersion("3.0.5.0", 0));

            // Assert
            Assert.IsFalse(v1Below);
            Assert.IsTrue(v1Equal);
            Assert.IsFalse(v2Below);
            Assert.IsTrue(v2Above);
            Assert.IsFalse(v3Below);
        }

        [TestMethod]
        public void IsVersionSupported_WithWildcardInVersion_ShouldReplaceWithHighValue()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:TestApp:1", "1.5.*" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);
            var version = new SemanticVersion("1.4.9.9", 0);

            // Act
            var result = MinVersions.IsVersionSupported("TestApp", version);

            // Assert
            Assert.IsFalse(result); // 1.4.9.9 is below 1.5.65535.65535
        }

        [TestMethod]
        public void IsVersionSupported_WithEmptyVersionString_ShouldReturnTrue()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:TestApp:1", "" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);
            var version = new SemanticVersion("1.0.0.0", 0);

            // Act
            var result = MinVersions.IsVersionSupported("TestApp", version);

            // Assert
            Assert.IsTrue(result); // Empty string becomes 65535.65535.65535.65535
        }

        [TestMethod]
        public void IsVersionSupported_WithCaseInsensitiveRegex_ShouldMatchAnyCase()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:TestApp:1", "1.5.0.0" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);
            var version = new SemanticVersion("1.6.0.0", 0);

            // Act
            var resultUpperCase = MinVersions.IsVersionSupported("TESTAPP", version);
            var resultMixedCase = MinVersions.IsVersionSupported("TeStApP", version);
            var resultLowerCase = MinVersions.IsVersionSupported("testapp", version);

            // Assert
            Assert.IsTrue(resultUpperCase);
            Assert.IsTrue(resultMixedCase);
            Assert.IsTrue(resultLowerCase);
        }

        [TestMethod]
        public void IsVersionSupported_WithInvalidMajorVersionInConfig_ShouldSkipEntry()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "MinVersions:TestApp:invalid", "1.5.0.0" }, // Not a valid int
                    { "MinVersions:TestApp:1", "1.5.0.0" }
                })
                .Build();

            MinVersions.Initialize(_loggerMock!.Object, configuration);
            var version = new SemanticVersion("1.4.0.0", 0);

            // Act
            var result = MinVersions.IsVersionSupported("TestApp", version);

            // Assert
            Assert.IsFalse(result); // Should only use valid entry (1: 1.5.0.0)
        }
    }
}
