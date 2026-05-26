using System.Text.Json.Serialization;

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
        public int PuntosRegistrados { get; set; }
        // NUEVAS PROPIEDADES
        public int TieneInicio { get; set; }
        public int TieneFin { get; set; }
    }
}
