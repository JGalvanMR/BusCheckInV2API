// ============================================================
//  ARCHIVO: BusCheckInV2API/Models/SincronizacionRequest.cs
//  INSTRUCCIÓN: Reemplaza el archivo completo.
//
//  BUG ORIGINAL:
//    El cliente (MAUI) serializa TimeSpan como "05:30:00" (string JSON).
//    Si el modelo declara TimeSpan, el binder de ASP.NET Core puede
//    fallar con formatos no estándar. Recibir como string y parsear
//    dentro del endpoint es el enfoque más robusto.
// ============================================================

namespace BusCheckInV2API.Models
{
    public class SincronizacionRequest
    {
        public int IdFletePer { get; set; }

        /// <summary>
        /// Fecha en formato ISO 8601: "2025-01-15" o "2025-01-15T00:00:00"
        /// Se parsea en el endpoint con DateTime.TryParse.
        /// </summary>
        public string Fecha { get; set; } = string.Empty;

        /// <summary>
        /// Hora en formato "HH:mm:ss" (ej: "05:30:00").
        /// System.Text.Json serializa TimeSpan en este formato.
        /// Se parsea en el endpoint con TimeSpan.TryParse.
        /// </summary>
        public string Hora { get; set; } = string.Empty;

        public string ProvClave { get; set; } = string.Empty;
        public int IdDestFlete { get; set; }
        public string TipoFlete { get; set; } = string.Empty;
        public string TipoViaje { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Chofer { get; set; }
        public int? CantidadReal { get; set; }
        public string? Observaciones { get; set; }
        public bool IsSynced { get; set; }
    }
}
