using System;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// The masks of the characters room at the size of their digitisation, which in real life is much bigger than the
    /// exhibition size every model is brought to (owner, 28/10/2026: «que sean mas como en la vida real, que son mucho mas
    /// grandes»). Sizes are set per record in one file.
    /// </summary>
    public sealed class MuseumPieceSizesTests
    {
        [TearDown] public void TearDown() => MuseumPieceSizes.Replace(null);

        [Test]
        public void TheMasksAreShownAtTheSizeOfTheirDigitisation()
        {
            var sizes = MuseumPieceSizes.Current;
            foreach (var slug in new[] { "matachin-mask", "sanjuan-animal-mask", "sanjuan-whistling-mask", "sanjuan-smooth-mask" })
                Assert.That(sizes.ScannedSize, Does.Contain(slug), slug + " keeps the size it was captured at.");
            // The Matachín mask was captured 1.13 m high (with its feathers and ribbons): 0.6 m brought it to about half.
            float natural = 1.1252f / ModelExaminationPose.ExhibitSize;
            Assert.That(sizes.ScaleFor("matachin-mask", natural) * ModelExaminationPose.ExhibitSize, Is.EqualTo(1.1252f).Within(1e-4f));
            Assert.That(sizes.ScaleFor("castillo", 10), Is.EqualTo(1), "Any other piece keeps the exhibition size.");
            Assert.That(sizes.ScaleFor(null, 2), Is.EqualTo(1));
        }

        [Test]
        public void ADigitisationThatCannotBeARealSizeKeepsTheExhibitionSize()
        {
            var sizes = MuseumPieceSizes.Parse("{\"schema_version\":1,\"scanned_size\":[\"mask\"]}");
            Assert.That(sizes.ScaleFor("mask", 40 / ModelExaminationPose.ExhibitSize), Is.EqualTo(1), "A 40 m scan is not a mask.");
            Assert.That(sizes.ScaleFor("mask", .01f / ModelExaminationPose.ExhibitSize), Is.EqualTo(1), "Nor a 1 cm one.");
            Assert.That(sizes.ScaleFor("mask", float.NaN), Is.EqualTo(1));
        }

        [Test]
        public void AMeasuredSizeSetsTheLongestSide()
        {
            var sizes = MuseumPieceSizes.Parse("{\"schema_version\":1,\"longest_side\":{\"mask\":1.2}}");
            Assert.That(sizes.ScaleFor("mask", 3) * ModelExaminationPose.ExhibitSize, Is.EqualTo(1.2f).Within(1e-5f),
                "Whatever the digitisation says.");
            Assert.Throws<InvalidOperationException>(() => MuseumPieceSizes.Parse("{\"schema_version\":1,\"longest_side\":{\"mask\":7}}"));
            Assert.Throws<InvalidOperationException>(() => MuseumPieceSizes.Parse("{\"schema_version\":1,\"longest_side\":{\"mask\":0}}"));
            Assert.Throws<InvalidOperationException>(() => MuseumPieceSizes.Parse(
                "{\"schema_version\":1,\"scanned_size\":[\"mask\"],\"longest_side\":{\"mask\":1}}"), "One size per piece.");
            Assert.Throws<InvalidOperationException>(() => MuseumPieceSizes.Parse("{\"schema_version\":2}"));
        }
    }
}
