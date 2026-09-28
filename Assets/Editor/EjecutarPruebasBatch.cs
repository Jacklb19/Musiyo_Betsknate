using System;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    public static class EjecutarPruebasBatch
    {
        public static void Ejecutar()
        {
            var observador = new Observador();
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(observador);
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode })
            {
                runSynchronously = true
            });
            if (!observador.Terminada || observador.Fallidas > 0 || observador.Aprobadas == 0)
                throw new InvalidOperationException("Las pruebas EditMode no finalizaron correctamente.");
        }

        private sealed class Observador : ICallbacks
        {
            public bool Terminada { get; private set; }
            public int Aprobadas { get; private set; }
            public int Fallidas { get; private set; }

            public void RunStarted(ITestAdaptor pruebas) { }
            public void TestStarted(ITestAdaptor prueba) { }

            public void TestFinished(ITestResultAdaptor resultado)
            {
                if (!resultado.HasChildren && resultado.ResultState.StartsWith("Failed"))
                    Debug.LogError(resultado.FullName + ": " + resultado.Message);
            }

            public void RunFinished(ITestResultAdaptor resultado)
            {
                Terminada = true;
                Aprobadas = resultado.PassCount;
                Fallidas = resultado.FailCount;
                Debug.Log("EditMode: " + Aprobadas + " aprobadas, " + Fallidas + " fallidas.");
            }
        }
    }
}
