// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Linq;
using System.Xml;
using Microsoft.Web.XmlTransform;

namespace Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms
{
    /// <summary>
    /// Custom applicationHost.xdt transform that ensures a semicolon-separated environment-variable list
    /// contains this extension's value:
    /// <list type="bullet">
    ///   <item>If the target <c>&lt;add name="..."&gt;</c> element does not exist, the transform node is inserted.</item>
    ///   <item>If it exists, the value is appended (semicolon-separated) only when it is not already one of
    ///   the entries, so re-installs / restarts never create duplicates.</item>
    /// </list>
    /// This lets the extension coexist with other agents that write the same variables (for example the
    /// Application Insights auto-instrumentation agent's <c>ASPNETCORE_HOSTINGSTARTUPASSEMBLIES</c> and
    /// <c>DOTNET_STARTUP_HOOKS</c>), rather than being skipped by the built-in <c>InsertIfMissing</c>.
    /// </summary>
    public class AppendListValueIfMissing : Transform
    {
        private const char Separator = ';';

        public AppendListValueIfMissing()
            : base(TransformFlags.UseParentAsTargetNode, MissingTargetMessage.None)
        {
        }

        protected override void Apply()
        {
            string valueToEnsure = GetAttributeValue(TransformNode, "value") ?? string.Empty;
            string name = GetAttributeValue(TransformNode, "name") ?? "(unknown)";

            // No existing <add name="..."> matched: insert our element.
            if (TargetChildNodes == null || TargetChildNodes.Count == 0)
            {
                TargetNode.AppendChild(TransformNode);
                Log.LogMessage("Inserted environment variable '{0}'.", name);
                return;
            }

            foreach (XmlNode node in TargetChildNodes)
            {
                XmlAttribute valueAttribute = FindAttribute(node, "value");
                if (valueAttribute == null)
                {
                    XmlAttribute created = node.OwnerDocument.CreateAttribute("value");
                    created.Value = valueToEnsure;
                    node.Attributes.Append(created);
                    Log.LogMessage("Set value for environment variable '{0}'.", name);
                    continue;
                }

                if (ListContainsValue(valueAttribute.Value, valueToEnsure))
                {
                    Log.LogMessage("Environment variable '{0}' already contains the value; leaving it unchanged.", name);
                    continue;
                }

                valueAttribute.Value = string.IsNullOrEmpty(valueAttribute.Value)
                    ? valueToEnsure
                    : valueAttribute.Value + Separator + valueToEnsure;
                Log.LogMessage("Appended value to environment variable '{0}'.", name);
            }
        }

        private static bool ListContainsValue(string listValue, string token)
        {
            if (string.IsNullOrEmpty(listValue))
            {
                return false;
            }

            return listValue
                .Split(Separator)
                .Any(entry => string.Equals(entry.Trim(), (token ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static string GetAttributeValue(XmlNode node, string attributeName) =>
            FindAttribute(node, attributeName)?.Value;

        private static XmlAttribute FindAttribute(XmlNode node, string attributeName) =>
            node?.Attributes?
                .Cast<XmlAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Name, attributeName, StringComparison.OrdinalIgnoreCase));
    }
}
