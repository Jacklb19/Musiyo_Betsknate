using System;
using System.Collections.Generic;
using MusiyoBetsknate.Dominio;
using MusiyoBetsknate.Museo;
using MusiyoBetsknate.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace MusiyoBetsknate.Tests
{
    public class PublicacionYSeleccionTests
    {
        private readonly List<UnityEngine.Object> objetos = new List<UnityEngine.Object>();
        private static readonly DateTimeOffset Ahora = DateTimeOffset.Parse("2026-09-20T12:00:00Z");
        private AutorizacionUso Permiso(UsoCultural uso, string revision = "r1",
            EstadoAutorizacion estado = EstadoAutorizacion.Autorizado,
            string inicio = "2020-01-01T00:00:00Z", string fin = "2099-01-01T00:00:00Z")
            => new AutorizacionUso(revision, uso, estado, inicio, fin);

        private RevisionPublicacion Revision(EstadoEditorial estado = EstadoEditorial.Aprobado,
            bool representacion = true, bool alternativas = true)
            => new RevisionPublicacion("r1", estado, representacion, alternativas,
                Permiso(UsoCultural.Publicar), Permiso(UsoCultural.Reutilizar));

        private Mascara Pieza(string nombre, bool publica = true)
        {
            var pieza = ScriptableObject.CreateInstance<Mascara>();
            objetos.Add(pieza);
            pieza.InicializarBase(nombre, nombre, "Dato sintético", "Prueba", "Texto de prueba");
            if (publica) pieza.AsignarRevisionPublicacion(Revision());
            return pieza;
        }

        private T Componente<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            objetos.Add(go);
            return go.AddComponent<T>();
        }

        [TearDown] public void Limpiar()
        {
            foreach (var objeto in objetos) UnityEngine.Object.DestroyImmediate(objeto);
            objetos.Clear();
        }

        [Test] public void RevisionCompletaPermitePublicar() => Assert.IsTrue(Revision().EsPublicableEn(Ahora));

        [TestCase(EstadoEditorial.Borrador)]
        [TestCase(EstadoEditorial.Pendiente)]
        [TestCase(EstadoEditorial.Restringido)]
        [TestCase(EstadoEditorial.Retirado)]
        public void EstadosNoPublicosDeniegan(EstadoEditorial estado)
            => Assert.IsFalse(Revision(estado).EsPublicableEn(Ahora));

        [TestCase(false, true)] [TestCase(true, false)]
        public void RevisionIncompletaDeniega(bool representacion, bool alternativas)
            => Assert.IsFalse(Revision(representacion: representacion, alternativas: alternativas).EsPublicableEn(Ahora));

        [TestCase(EstadoAutorizacion.Pendiente)]
        [TestCase(EstadoAutorizacion.Denegado)]
        [TestCase(EstadoAutorizacion.Revocado)]
        public void PermisosNoAutorizadosDeniegan(EstadoAutorizacion estado)
            => Assert.IsFalse(Permiso(UsoCultural.Publicar, estado: estado).Permite("r1", UsoCultural.Publicar, Ahora));

        [TestCase("2026-09-20T12:00:00Z", "2026-09-21T12:00:00Z", true)]
        [TestCase("2026-09-19T12:00:00Z", "2026-09-20T12:00:00Z", false)]
        [TestCase("2026-09-21T12:00:00Z", "2026-09-22T12:00:00Z", false)]
        [TestCase("2026-09-19T12:00:00", "2099-01-01T00:00:00Z", false)]
        [TestCase("invalida", "2099-01-01T00:00:00Z", false)]
        [TestCase("2026-09-19T12:00:00Z", "", false)]
        [TestCase("2026-09-20T07:00:00-05:00", "2026-09-21T07:00:00-05:00", true)]
        public void EvaluaIntervaloConZona(string inicio, string fin, bool esperado)
            => Assert.AreEqual(esperado, Permiso(UsoCultural.Publicar, inicio: inicio, fin: fin)
                .Permite("r1", UsoCultural.Publicar, Ahora));

        [Test] public void PermisoNoSeTransfiereEntreRevisionesNiUsos()
        {
            var permiso = Permiso(UsoCultural.Publicar);
            Assert.IsFalse(permiso.Permite("r2", UsoCultural.Publicar, Ahora));
            Assert.IsFalse(permiso.Permite("r1", UsoCultural.Reutilizar, Ahora));
            Assert.IsFalse(new RevisionPublicacion("r1", EstadoEditorial.Aprobado, true, true,
                permiso, null).EsPublicableEn(Ahora));
        }

        [Test] public void AprobacionLegadaNoAutorizaYEditarInvalida()
        {
            var pieza = Pieza("Prueba");
            pieza.InicializarBase("id", "Cambio", "", "", "", consentimiento:
                new ConsentimientoCultural(EstadoConsentimiento.Aprobado));
            Assert.IsFalse(pieza.EsDivulgablePublicamente);
        }

        [Test] public void ListaOrdenadaFiltraNulosDuplicadosYRestringidos()
        {
            var punto = Componente<PuntoInteres>();
            var a = Pieza("A"); var b = Pieza("B"); var privada = Pieza("Secreto", false);
            punto.AsignarElementos(new[] { a, null, privada, b, a });
            CollectionAssert.AreEqual(new[] { a, b }, punto.ObtenerElementosPublicables());
            Assert.IsTrue(punto.CambiarElemento(1));
            Assert.AreSame(b, punto.ElementoCultural);
            StringAssert.Contains("2 de 2", punto.ObtenerAnuncioAccesible());
            Assert.IsTrue(punto.CambiarElemento(1));
            Assert.AreSame(a, punto.ElementoCultural);
            Assert.IsFalse(punto.SeleccionarElemento(2));
        }

        [Test] public void UnaPiezaPuedeEstarEnDosPuntosYSeRetiraDeAmbos()
        {
            var a = Componente<PuntoInteres>(); var b = Componente<PuntoInteres>();
            var pieza = Pieza("Compartida");
            a.AsignarElemento(pieza); b.AsignarElemento(pieza);
            Assert.IsTrue(a.Examinar()); Assert.IsTrue(b.Examinar());
            pieza.AsignarRevisionPublicacion(Revision(EstadoEditorial.Retirado));
            Assert.IsFalse(a.Examinar()); Assert.IsFalse(b.Examinar());
            Assert.IsEmpty(a.ObtenerElementosPublicables());
            StringAssert.DoesNotContain("Compartida", a.ObtenerAnuncioAccesible());
        }

        [Test] public void PuntoInactivoNoEmiteExamen()
        {
            var punto = Componente<PuntoInteres>();
            punto.AsignarElemento(Pieza("A"));
            punto.Desactivar();
            Assert.IsFalse(punto.Examinar());
        }

        [Test] public void AsignacionVaciaNoRecuperaContenidoAnterior()
        {
            var punto = Componente<PuntoInteres>();
            punto.AsignarElemento(Pieza("A"));
            punto.AsignarElementos(null);
            Assert.IsNull(punto.ElementoCultural);
            Assert.IsFalse(punto.Examinar());
        }

        [Test] public void VisorLimpiaTextoAlRetirarYAlCerrar()
        {
            var visor = Componente<VisorFichaCulturalUI>();
            var texto = Componente<TextMeshProUGUI>();
            var serializado = new SerializedObject(visor);
            serializado.FindProperty("textoNombre").objectReferenceValue = texto;
            serializado.ApplyModifiedPropertiesWithoutUndo();
            var pieza = Pieza("Texto público");
            visor.MostrarElemento(pieza);
            Assert.AreEqual("Texto público", texto.text);
            pieza.AsignarRevisionPublicacion(null);
            // EditMode no despacha mensajes de ciclo de vida a MonoBehaviours normales.
            typeof(VisorFichaCulturalUI).GetMethod("Update",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(visor, null);
            Assert.IsEmpty(texto.text);
            visor.MostrarElemento(Pieza("Otra ficha"));
            visor.Cerrar();
            Assert.IsEmpty(texto.text);
        }
    }
}
