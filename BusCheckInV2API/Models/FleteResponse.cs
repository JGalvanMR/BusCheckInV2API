// BusCheckInV2API/Models/FleteResponse.cs
// Modelo del BACKEND (no del cliente MAUI). Es la clase que el controller
// usa para poblar el JSON de respuesta en ObtenerFletesPorChofer y
// ObtenerFletesPorChoferTodos.
//
// FIX 2026-06-03 (Opción A): se agregan 3 propiedades para que coincidan
// con el SELECT del controller (que ya las devuelve desde la BD). Sin
// estas propiedades, el código del controller compila con error CS0117
// porque está intentando asignar CantPasajeros, UltimaFechaDetalle y
// EstadoCalculado, que no existen en la clase.
//
// Si tu proyecto YA TIENE un FleteResponse.cs en BusCheckInV2API/Models/,
// este archivo es la versión que DEBÉS usar (con las 3 propiedades
// agregadas). Si tu versión difiere, comparalas y agregá las 3 props.

using System;

namespace BusCheckInV2API.Models
{
    public class FleteResponse
    {
        public int IdFletePer { get; set; }
        public string Fecha { get; set; }
        public string Hora { get; set; }
        public string ProveedorClave { get; set; }
        public string ProveedorNombre { get; set; }
        public int IdDestFlete { get; set; }
        public string RutaNombre { get; set; }
        public string TipoFlete { get; set; }
        public string TipoViaje { get; set; }
        public int Cantidad { get; set; }
        public string Estatus { get; set; }
        public string Chofer { get; set; }
        public int? CantidadReal { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string Observaciones { get; set; }
        public int PuntosRegistrados { get; set; }

        // ── FIX 2026-06-03 (Opción A) ──────────────────────────────────────
        // Estos 3 campos son calculados por el SELECT del controller
        // (no persisten en la BD). Si no existen, el controller no
        // compila con error CS0117.

        /// <summary>Cantidad de pasajeros (CveNomina NOT IN 0, 9999).</summary>
        public int CantPasajeros { get; set; }

        /// <summary>Timestamp del último registro de detalle del flete.</summary>
        public DateTime? UltimaFechaDetalle { get; set; }

        /// <summary>
        /// Estado conceptual derivado de los detalles:
        /// "Activo" | "En curso" | "Pendiente" | "Finalizado" | "Cancelado"
        /// </summary>
        public string EstadoCalculado { get; set; } = "Activo";

        // ── FIX 2026-06-02 (Nivel 2 #19+#23) ──────────────────────────────
        // Mantenemos estos 2 por retrocompat con clientes que ya los usan.
        public int TieneInicio { get; set; }
        public int TieneFin { get; set; }
        public int EsPendiente { get; set; }
    }
}
