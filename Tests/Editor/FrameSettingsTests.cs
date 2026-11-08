using NUnit.Framework;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class FrameSettingsTests
    {
        [Test]
        public void Default_IsAllEnabled_ReflectionDefaultsSkipScreenSpace()
        {
            foreach (EFrameSetting setting in FrameSettingsUtility.All)
                Assert.IsTrue(FrameSettings.AllEnabled.IsEnabled(setting), setting.ToString());
            FrameSettings reflection = FrameSettings.ReflectionDefaults;
            Assert.IsFalse(reflection.IsEnabled(EFrameSetting.ScreenSpaceReflection));
            Assert.IsFalse(reflection.IsEnabled(EFrameSetting.ContactShadows));
            Assert.IsTrue(reflection.IsEnabled(EFrameSetting.ReflectionProbes));
            Assert.IsTrue(reflection.IsEnabled(EFrameSetting.VolumetricFog));
        }

        [Test]
        public void Resolve_PicksBaselineByCameraKind_AndAppliesOnlyOverriddenSettings()
        {
            FrameSettings defaults = FrameSettings.AllEnabled;
            defaults.SetEnabled(EFrameSetting.VolumetricClouds, false);
            FrameSettings reflection = FrameSettings.ReflectionDefaults;

            var cameraOverride = new FrameSettingsOverride();
            cameraOverride.SetOverride(EFrameSetting.ScreenSpaceReflection, true, false);
            cameraOverride.SetOverride(EFrameSetting.VolumetricClouds, true, true);

            ResolvedFrameSettings game = FrameSettingsUtility.Resolve(defaults, reflection, false, true, cameraOverride);
            Assert.IsFalse(game.IsEnabled(EFrameSetting.ScreenSpaceReflection));
            Assert.IsTrue(game.IsEnabled(EFrameSetting.VolumetricClouds), "The override re-enables what the asset disabled.");
            Assert.IsTrue(game.IsEnabled(EFrameSetting.Decals));
            Assert.AreEqual(EFrameSettingSource.CameraOverride, game.SourceOf(EFrameSetting.ScreenSpaceReflection));
            Assert.AreEqual(EFrameSettingSource.AssetDefault, game.SourceOf(EFrameSetting.Decals));

            ResolvedFrameSettings withoutOverride = FrameSettingsUtility.Resolve(defaults, reflection, false, false, cameraOverride);
            Assert.IsFalse(withoutOverride.IsEnabled(EFrameSetting.VolumetricClouds), "Custom Frame Settings off ignores the stored overrides.");

            ResolvedFrameSettings probe = FrameSettingsUtility.Resolve(defaults, reflection, true, false, default);
            Assert.IsFalse(probe.IsEnabled(EFrameSetting.ContactShadows));
            Assert.AreEqual(EFrameSettingSource.ReflectionDefault, probe.SourceOf(EFrameSetting.ContactShadows));
        }
    }
}
