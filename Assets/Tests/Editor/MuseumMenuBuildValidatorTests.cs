using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MusiyoBetsknate.Tests
{
    public class MuseumMenuBuildValidatorTests
    {
        [TestCase("matching", true)]
        [TestCase("missing", false)]
        [TestCase("empty", false)]
        [TestCase("changed", false)]
        [TestCase("wrong-folder", false)]
        public void AndroidMenuMustMatchTheGeneratedResourceBytes(string scenario, bool expected)
        {
            var folder = Path.Combine(Path.GetTempPath(), "musiyo-menu-artifact-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                const string guid = "1234567890abcdef1234567890abcdef";
                var resource = Path.Combine(folder, guid);
                var apk = Path.Combine(folder, "Museum.apk");
                File.WriteAllText(resource, "synthetic menu resource");
                using (var bundle = ZipFile.Open(apk, ZipArchiveMode.Create))
                {
                    var key = scenario == "missing" ? "another-guid" : guid;
                    var entry = bundle.CreateEntry((scenario == "wrong-folder" ? "assets/elsewhere/" : "assets/bin/Data/") + key);
                    using var stream = entry.Open();
                    var content = scenario == "empty" ? "" : scenario == "changed" ? "synthetic menu modified" : "synthetic menu resource";
                    var bytes = Encoding.UTF8.GetBytes(content);
                    stream.Write(bytes, 0, bytes.Length);
                }
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("MusiyoBetsknate.Editor.MuseumMenuBuildValidator"))
                    .First(candidate => candidate != null);
                var actual = (bool)type.GetMethod("AndroidResourceMatches").Invoke(null, new object[] { apk, resource, guid });
                Assert.That(actual, Is.EqualTo(expected));
            }
            finally
            {
                var absolute = Path.GetFullPath(folder);
                if (absolute.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(absolute, true);
            }
        }
    }
}
