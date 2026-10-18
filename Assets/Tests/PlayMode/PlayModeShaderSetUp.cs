using NUnit.Framework;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Play mode tests draw with every shader compiled. With asynchronous compilation the editor draws a cyan stand-in
    /// for a shader still compiling, which once turned the maize blue in far captures.
    /// </summary>
    [SetUpFixture]
    public sealed class PlayModeShaderSetUp
    {
#if UNITY_EDITOR
        private bool asynchronous;

        [OneTimeSetUp]
        public void CompileShadersBeforeDrawing()
        {
            asynchronous = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
        }

        [OneTimeTearDown]
        public void Restore() => UnityEditor.ShaderUtil.allowAsyncCompilation = asynchronous;
#endif
    }
}
