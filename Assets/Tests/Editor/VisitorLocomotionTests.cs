using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class VisitorLocomotionTests
    {
        [Test]
        public void WalkingEasesInAndStopsWithTheConfiguredRates()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var target = Vector3.forward * settings.WalkSpeed;
            var velocity = DesktopVisitorController.StepVelocity(Vector3.zero, target, settings.Acceleration, settings.Deceleration, .1f);
            Assert.That(velocity.magnitude, Is.EqualTo(Mathf.Min(settings.WalkSpeed, settings.Acceleration * .1f)).Within(.0001f));
            for (int frame = 0; frame < 100; frame++)
                velocity = DesktopVisitorController.StepVelocity(velocity, target, settings.Acceleration, settings.Deceleration, .02f);
            Assert.That(velocity, Is.EqualTo(target));
            var stopping = DesktopVisitorController.StepVelocity(velocity, Vector3.zero, settings.Acceleration, settings.Deceleration, .05f);
            Assert.That(stopping.magnitude, Is.EqualTo(Mathf.Max(0, settings.WalkSpeed - settings.Deceleration * .05f)).Within(.0001f));
        }

        [Test]
        public void ReversingUsesDecelerationAndInvalidTimeKeepsVelocity()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var current = Vector3.forward * settings.WalkSpeed;
            var reversed = DesktopVisitorController.StepVelocity(current, -current, settings.Acceleration, settings.Deceleration, .01f);
            Assert.That(current.z - reversed.z, Is.EqualTo(settings.Deceleration * .01f).Within(.0001f));
            Assert.That(DesktopVisitorController.StepVelocity(current, Vector3.zero, 1, 1, float.NaN), Is.EqualTo(current));
            Assert.That(DesktopVisitorController.StepVelocity(current, Vector3.zero, 1, 1, -1), Is.EqualTo(current));
        }

        [Test]
        public void ConfiguredSpeedsKeepTheDesktopComfortDefaults()
        {
            var settings = MuseumExperienceConfiguration.Current;
            Assert.That(settings.WalkSpeed, Is.EqualTo(2f));
            Assert.That(settings.SprintSpeed, Is.EqualTo(3f));
            Assert.That(settings.MaximumPitch, Is.LessThan(90f));
        }
    }
}
