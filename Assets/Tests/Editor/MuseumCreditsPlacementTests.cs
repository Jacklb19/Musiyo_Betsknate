using MusiyoBetsknate.Museo;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>Where the closing credits stand by the fire, in a headset and on a screen.</summary>
    public sealed class MuseumCreditsPlacementTests
    {
        private GameObject set, pose, eyes, view;

        [SetUp]
        public void SetUp()
        {
            MuseumInput.Replace(MuseumInput.CreateDefault());
            set = new GameObject("CreditsTestSet");
            set.transform.position = new Vector3(0, -400, 0);
            pose = new GameObject("CreditsTestPose");
            pose.transform.SetPositionAndRotation(new Vector3(-.35f, -398.75f, -3.6f), Quaternion.Euler(12, 10, 0));
            eyes = new GameObject("CreditsTestEyes");
            eyes.transform.SetPositionAndRotation(new Vector3(-.35f, -400 + 1.65f, -3.6f), Quaternion.Euler(0, 10, 0));
            view = new GameObject("CreditsTestCamera", typeof(Camera));
        }

        [TearDown]
        public void TearDown()
        {
            if (MuseumInput.Current.Spatial) MuseumInput.Current.SetSpatial(false);
            MuseumInput.Replace(null);
            foreach (var item in new[] { set, pose, eyes, view }) Object.DestroyImmediate(item);
        }

        private MuseumCreditsRoll Roll() => MuseumCreditsRoll.Create(set.transform, pose.transform, eyes.transform, view.GetComponent<Camera>(),
            new MuseumCreditsContractV1
            {
                schema_version = 1, title = "Museo", subtitle = "Créditos",
                sections = new[] { new CreditSectionContractV1 { key = "team", title = "Equipo", lines = new[] { new CreditLineContractV1 { role = "Rol", text = "Nombre" } } } },
            }, null);

        private static (float distance, float angle) Seen(Transform eyes, Transform roll)
        {
            var flat = Vector3.ProjectOnPlane(roll.position - eyes.position, Vector3.up);
            return (flat.magnitude, Vector3.SignedAngle(Vector3.ProjectOnPlane(eyes.forward, Vector3.up), flat, Vector3.up));
        }

        /// <summary>Owner, 22/10: in the headset the credits stood 3 m away and almost 1 m to the left, and could not be read.</summary>
        [Test]
        public void InAHeadsetTheCreditsStandCloserNearerTheMiddleAndLarger()
        {
            var screen = Roll();
            var (screenDistance, screenAngle) = Seen(eyes.transform, screen.transform);
            float screenLetters = screen.transform.lossyScale.x / screenDistance;
            Object.DestroyImmediate(screen.gameObject);
            MuseumInput.Current.SetSpatial(true, set.transform);
            var headset = Roll();
            var (distance, angle) = Seen(eyes.transform, headset.transform);
            Assert.That(distance, Is.InRange(1.6f, 1.8f), "About 1.6 to 1.8 m from the eyes.");
            Assert.That(angle, Is.InRange(-20f, -5f), "A little to the left of the gaze, so the fire stays in view.");
            Assert.That(Mathf.Abs(angle), Is.LessThan(Mathf.Abs(screenAngle)), "Nearer the middle than before.");
            float letters = headset.transform.lossyScale.x / distance;
            Assert.That(letters, Is.GreaterThan(screenLetters * 1.3f), "The letters look larger.");
            Assert.That(headset.transform.position.y, Is.InRange(eyes.transform.position.y - .3f, eyes.transform.position.y), "Just under the eyes.");
            Assert.That(Vector3.Angle(headset.transform.forward, Vector3.ProjectOnPlane(headset.transform.position - eyes.transform.position, Vector3.up)),
                Is.LessThan(5), "It faces the eyes.");
            var corners = new Vector3[4];
            ((RectTransform)headset.transform).GetWorldCorners(corners);
            float right = Vector3.SignedAngle(Vector3.ProjectOnPlane(eyes.transform.forward, Vector3.up),
                Vector3.ProjectOnPlane(corners[2] - eyes.transform.position, Vector3.up), Vector3.up);
            Assert.That(right, Is.LessThan(6), "Its right edge leaves the fire on the right free.");
        }
    }
}
