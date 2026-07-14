// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Web.XmlTransform;
using Xunit;

namespace Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms.Tests
{
    /// <summary>
    /// Drives the real <see cref="XmlTransformation"/> engine (the same one the App Service
    /// applicationHost.xdt host uses) against the custom <c>AppendListValueIfMissing</c> transform.
    /// </summary>
    public class AppendListValueIfMissingTests
    {
        // Imports the transform assembly by simple name so XDT can resolve the custom transform type.
        private const string ImportHeader =
            "<configuration xmlns:xdt=\"http://schemas.microsoft.com/XML-Document-Transform\">" +
            "<xdt:Import assembly=\"Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms\" " +
            "namespace=\"Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms\" />";

        [Fact]
        public void Insert_WhenNoMatchingAdd_InsertsElement()
        {
            // Regression for the cross-document bug: the parent <environmentVariables> exists but no
            // matching <add> does, so the transform takes its insert path. Appending the transform node
            // directly (without ImportNode) threw a cross-document ArgumentException.
            XmlTransformableDocument target = Load(
                "<configuration><system.webServer><runtime><environmentVariables />" +
                "</runtime></system.webServer></configuration>");

            string transform = ImportHeader +
                "<system.webServer><runtime><environmentVariables>" +
                "<add name=\"DOTNET_STARTUP_HOOKS\" value=\"C:\\payload\\hook.dll\" " +
                "xdt:Locator=\"Match(name)\" xdt:Transform=\"AppendListValueIfMissing\" />" +
                "</environmentVariables></runtime></system.webServer></configuration>";

            bool result = Apply(target, transform);

            Assert.True(result);
            XmlNode add = target.SelectSingleNode("//environmentVariables/add[@name='DOTNET_STARTUP_HOOKS']");
            Assert.NotNull(add);
            Assert.Equal("C:\\payload\\hook.dll", add.Attributes["value"].Value);
        }

        [Fact]
        public void Append_WhenValueMissingFromExistingList_AppendsSemicolonSeparated()
        {
            XmlTransformableDocument target = Load(
                "<configuration><system.webServer><runtime><environmentVariables>" +
                "<add name=\"DOTNET_STARTUP_HOOKS\" value=\"C:\\other\\agent.dll\" />" +
                "</environmentVariables></runtime></system.webServer></configuration>");

            string transform = ImportHeader +
                "<system.webServer><runtime><environmentVariables>" +
                "<add name=\"DOTNET_STARTUP_HOOKS\" value=\"C:\\payload\\hook.dll\" " +
                "xdt:Locator=\"Match(name)\" xdt:Transform=\"AppendListValueIfMissing\" />" +
                "</environmentVariables></runtime></system.webServer></configuration>";

            bool result = Apply(target, transform);

            Assert.True(result);
            XmlNode add = target.SelectSingleNode("//environmentVariables/add[@name='DOTNET_STARTUP_HOOKS']");
            Assert.Equal("C:\\other\\agent.dll;C:\\payload\\hook.dll", add.Attributes["value"].Value);
        }

        [Fact]
        public void Append_WhenValueAlreadyPresent_LeavesListUnchanged()
        {
            XmlTransformableDocument target = Load(
                "<configuration><system.webServer><runtime><environmentVariables>" +
                "<add name=\"DOTNET_STARTUP_HOOKS\" value=\"C:\\payload\\hook.dll\" />" +
                "</environmentVariables></runtime></system.webServer></configuration>");

            string transform = ImportHeader +
                "<system.webServer><runtime><environmentVariables>" +
                "<add name=\"DOTNET_STARTUP_HOOKS\" value=\"C:\\payload\\hook.dll\" " +
                "xdt:Locator=\"Match(name)\" xdt:Transform=\"AppendListValueIfMissing\" />" +
                "</environmentVariables></runtime></system.webServer></configuration>";

            bool result = Apply(target, transform);

            Assert.True(result);
            XmlNode add = target.SelectSingleNode("//environmentVariables/add[@name='DOTNET_STARTUP_HOOKS']");
            // No duplicate is added when the value is already one of the entries.
            Assert.Equal("C:\\payload\\hook.dll", add.Attributes["value"].Value);
        }

        private static XmlTransformableDocument Load(string xml)
        {
            XmlTransformableDocument document = new() { PreserveWhitespace = true };
            document.LoadXml(xml);
            return document;
        }

        private static bool Apply(XmlTransformableDocument target, string transformXml)
        {
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(transformXml));
            using XmlTransformation transformation = new(stream, null);
            return transformation.Apply(target);
        }
    }
}
