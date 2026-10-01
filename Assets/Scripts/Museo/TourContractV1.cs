using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class TourContractV1
    {

        public static bool TryParse(string json, out TourContractV1 contract, out string error)
        {
            contract = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "El contrato está vacío.";
                return false;
            }

            try { contract = ContractJsonV1.Parse<TourContractV1>(json, ContractSchemasV1.Tour); }
            catch (JsonException) { error = "El contrato no tiene una estructura JSON compatible."; return false; }
            return contract != null && contract.Validate(out error);
        }

        public bool Validate(out string error)
        {
            error = null;
            if (schema_version != 1 || tour == null || string.IsNullOrWhiteSpace(tour.key) || rooms == null || rooms.Length == 0)
            {
                error = "Versión, identificador o salas del recorrido inválidos.";
                return false;
            }

            var roomKeys = new HashSet<string>(StringComparer.Ordinal);
            var anchorKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int roomIndex = 0; roomIndex < rooms.Length; roomIndex++)
            {
                var room = rooms[roomIndex];
                if (room == null || string.IsNullOrWhiteSpace(room.key) || room.order < 0
                    || (roomIndex > 0 && room.order <= rooms[roomIndex - 1].order)
                    || !roomKeys.Add(room.key) || room.points == null)
                {
                    error = "Sala inválida o repetida en el contrato.";
                    return false;
                }

                var previousOrder = -1;
                foreach (var point in room.points)
                {
                    if (point == null || string.IsNullOrWhiteSpace(point.key) || point.order <= previousOrder
                        || !anchorKeys.Add(point.key) || point.elements == null)
                    {
                        error = "Punto o anclaje inválido o repetido.";
                        return false;
                    }

                    var elementIds = new HashSet<string>(StringComparer.Ordinal);
                    previousOrder = point.order;
                    foreach (var element in point.elements)
                    {
                        if (element == null || string.IsNullOrWhiteSpace(element.slug) || !elementIds.Add(element.slug))
                        {
                            error = "Identificador de elemento inválido o repetido dentro del punto.";
                            return false;
                        }
                    }
                }
            }
            if (guide != null && !roomKeys.Contains(guide.room_key))
            {
                error = "La sala del guía no existe en el recorrido.";
                return false;
            }
            return true;
        }

        public bool MatchesScene(RecorridoVirtual sceneTour, out string error)
        {
            if (!Validate(out error)) return false;
            if (sceneTour == null || sceneTour.IdComponente != tour.key || sceneTour.Salas.Count != rooms.Length)
            {
                error = "El recorrido de Unity no coincide con el contrato.";
                return false;
            }

            var scenesByKey = new Dictionary<string, Escena>(StringComparer.Ordinal);
            foreach (var scene in sceneTour.Salas)
            {
                if (scenesByKey.ContainsKey(scene.IdComponente))
                {
                    error = "Hay salas repetidas en Unity.";
                    return false;
                }
                scenesByKey.Add(scene.IdComponente, scene);
            }

            for (int roomIndex = 0; roomIndex < rooms.Length; roomIndex++)
            {
                var declared = rooms[roomIndex];
                if (!scenesByKey.TryGetValue(declared.key, out var scene))
                {
                    error = "Falta la sala " + declared.key + " en Unity.";
                    return false;
                }
                if (sceneTour.Salas[roomIndex] != scene)
                {
                    error = "El orden de salas de Unity no coincide con el contrato.";
                    return false;
                }

                var sceneKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in scene.Hijos)
                    if (child is PuntoInteres point && !sceneKeys.Add(point.IdComponente))
                    {
                        error = "Hay anclajes repetidos en Unity.";
                        return false;
                    }

                if (sceneKeys.Count != declared.points.Length)
                {
                    error = "El número de puntos de la sala " + declared.key + " no coincide.";
                    return false;
                }
                foreach (var point in declared.points)
                    if (!sceneKeys.Contains(point.key))
                    {
                        error = "Falta el anclaje " + point.key + " en Unity.";
                        return false;
                    }
            }
            return true;
        }
    }

}
