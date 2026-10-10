using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    public static class VerifyMuseumMenuDelivery
    {
        public static void Run()
        {
            EjecutarPruebasBatch.Ejecutar();
            ValidateMuseumScene.ValidateSavedBlockout();
            BuildMuseumWeb.Build();
            Debug.Log("Museum menu delivery checks completed.");
        }
    }
}
