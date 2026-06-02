// BusCheckInV2/Models/FleteResponse.cs
// Modelo del cliente (MAUI) que coincide con la respuesta del backend
// WSBusCheckInV2Controller.ObtenerFletesPorChofer / ObtenerFletesPorChoferTodos.
//
// Si ya tienes este modelo en tu proyecto, COMPARA contra tu versión:
//   - Si te falta el campo EsPendiente, agrégalo (es el fix crítico del backend).
//   - Si te sobran campos (no listados aquí), déjalos como están: el
//     deserializador de System.Text.Json ignora silenciosamente los campos
//     del JSON que no existan en la clase C#.
//   - Si te faltan campos distintos a EsPendiente, agrégalos también
//     manteniendo los nombres EXACTOS que ves en el JSON del backend.

using System;

namespace BusCheckInV2.Models
{
    public class FleteResponse
    {
        // ── Identidad ──────────────────────────────────────────────────────
        public int IdFletePer { get; set; }

        // ── Tiempos (vienen como string desde SQL Server) ──────────────────
        public string Fecha { get; set; } = "";
        public string Hora { get; set; } = "";

        // ── Relaciones con catálogo ────────────────────────────────────────
        public string ProveedorClave { get; set; } = "";
        public string ProveedorNombre { get; set; } = "";
        public int IdDestFlete { get; set; }
        public string RutaNombre { get; set; } = "";

        // ── Datos operativos ───────────────────────────────────────────────
        public string TipoFlete { get; set; } = "";
        public string TipoViaje { get; set; } = "";
        public int Cantidad { get; set; }
        public string Estatus { get; set; } = "";
        public string Chofer { get; set; } = "";

        // ── Derivados en backend (antes se calculaban en cliente) ──────────
        public int PuntosRegistrados { get; set; }
        public int TieneInicio { get; set; }
        public int TieneFin { get; set; }

        // ── FIX 2026-06-02 (Nivel 2 #19 + #23) ─────────────────────────────
        // El backend ahora expone EsPendiente derivado en el SELECT.
        // Valor: 1 si el flete está pendiente de cerrar, 0 si no.
        // Lógica del backend: (Status IN ('P','I'))
        //                  OR (TieneInicio = 1 AND TieneFin = 0)
        //
        // Si tu versión anterior NO tiene este campo, agrégalo y la UI
        // del FletesPendientes podrá bindear directamente a él en vez de
        // recalcularlo con el helper EsFletePendiente del ViewModel.
        public int EsPendiente { get; set; }
    }
}
