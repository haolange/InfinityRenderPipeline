using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class DebugViewModeTests
    {
        const string ModesPath = "Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/DebugViewModes.hlsl";

        [Test]
        [Category("Unverified_0_5")]
        public void ShaderDefines_MatchEDebugView()
        {
            string text = File.ReadAllText(Path.GetFullPath(ModesPath));
            foreach (EDebugView view in Enum.GetValues(typeof(EDebugView)))
            {
                string name = "DEBUGVIEW_" + view.ToString().ToUpperInvariant();
                Match match = Regex.Match(text, @"#define\s+" + name + @"\s+(\d+)");
                Assert.IsTrue(match.Success, $"{ModesPath} is missing {name}.");
                Assert.AreEqual((int)view, int.Parse(match.Groups[1].Value), $"{name} disagrees with EDebugView.{view}.");
            }

            int defines = Regex.Matches(text, @"#define\s+DEBUGVIEW_\w+\s+\d+").Count;
            Assert.AreEqual(Enum.GetValues(typeof(EDebugView)).Length, defines, "Every DEBUGVIEW_* define must map to an EDebugView value.");
        }
    }
}
