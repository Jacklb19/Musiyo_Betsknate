using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusiyoBetsknate.Museo
{
    // Solo describe anclajes y referencias estables. La API decide qué contenido se puede entregar.
    [Serializable]
    public sealed class ContratoRecorridoV1
    {
        public int schemaVersion;
        public string recorridoId;
        public SalaContratoV1[] salas;

        public static bool IntentarLeer(string json, out ContratoRecorridoV1 contrato, out string error)
        {
            contrato = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "El contrato está vacío.";
                return false;
            }

            try { contrato = JsonUtility.FromJson<ContratoRecorridoV1>(json); }
            catch (ArgumentException) { error = "El contrato no es JSON válido."; return false; }
            return contrato != null && contrato.Validar(out error);
        }

        public bool Validar(out string error)
        {
            error = null;
            if (schemaVersion != 1 || string.IsNullOrWhiteSpace(recorridoId) || salas == null || salas.Length == 0)
            {
                error = "Versión, identificador o salas del recorrido inválidos.";
                return false;
            }

            var idsSalas = new HashSet<string>(StringComparer.Ordinal);
            var ordenes = new HashSet<int>();
            var anclajes = new HashSet<string>(StringComparer.Ordinal);
            for (int indiceSala = 0; indiceSala < salas.Length; indiceSala++)
            {
                var sala = salas[indiceSala];
                if (sala == null || string.IsNullOrWhiteSpace(sala.id) || sala.orden < 0
                    || sala.orden != indiceSala || !idsSalas.Add(sala.id)
                    || !ordenes.Add(sala.orden) || sala.puntos == null)
                {
                    error = "Sala inválida o repetida en el contrato.";
                    return false;
                }

                foreach (var punto in sala.puntos)
                {
                    if (punto == null || string.IsNullOrWhiteSpace(punto.anclajeId)
                        || !anclajes.Add(punto.anclajeId) || punto.elementoIds == null)
                    {
                        error = "Punto o anclaje inválido o repetido.";
                        return false;
                    }

                    var idsElementos = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var id in punto.elementoIds)
                    {
                        if (string.IsNullOrWhiteSpace(id) || !idsElementos.Add(id))
                        {
                            error = "Identificador de elemento inválido o repetido dentro del punto.";
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public bool CoincideCon(RecorridoVirtual recorrido, out string error)
        {
            if (!Validar(out error)) return false;
            if (recorrido == null || recorrido.IdComponente != recorridoId || recorrido.Salas.Count != salas.Length)
            {
                error = "El recorrido de Unity no coincide con el contrato.";
                return false;
            }

            var salasPorId = new Dictionary<string, Escena>(StringComparer.Ordinal);
            foreach (var sala in recorrido.Salas)
            {
                if (salasPorId.ContainsKey(sala.IdComponente))
                {
                    error = "Hay salas repetidas en Unity.";
                    return false;
                }
                salasPorId.Add(sala.IdComponente, sala);
            }

            for (int indiceSala = 0; indiceSala < salas.Length; indiceSala++)
            {
                var declarada = salas[indiceSala];
                if (!salasPorId.TryGetValue(declarada.id, out var sala))
                {
                    error = "Falta la sala " + declarada.id + " en Unity.";
                    return false;
                }
                if (recorrido.Salas[indiceSala] != sala)
                {
                    error = "El orden de salas de Unity no coincide con el contrato.";
                    return false;
                }

                var puntos = new HashSet<string>(StringComparer.Ordinal);
                foreach (var hijo in sala.Hijos)
                    if (hijo is PuntoInteres punto && !puntos.Add(punto.IdComponente))
                    {
                        error = "Hay anclajes repetidos en Unity.";
                        return false;
                    }

                if (puntos.Count != declarada.puntos.Length)
                {
                    error = "El número de puntos de la sala " + declarada.id + " no coincide.";
                    return false;
                }
                foreach (var punto in declarada.puntos)
                    if (!puntos.Contains(punto.anclajeId))
                    {
                        error = "Falta el anclaje " + punto.anclajeId + " en Unity.";
                        return false;
                    }
            }
            return true;
        }
    }

    [Serializable]
    public sealed class SalaContratoV1
    {
        public string id;
        public int orden;
        public PuntoContratoV1[] puntos;
    }

    [Serializable]
    public sealed class PuntoContratoV1
    {
        public string anclajeId;
        public string[] elementoIds;
    }
}
