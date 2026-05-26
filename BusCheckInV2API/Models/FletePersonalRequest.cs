namespace BusCheckInV2API.Models
{
    public class FletePersonalRequest
    {
        public string Fecha { get; set; }
        public string Hora { get; set; }
        public string ClaveProveedor { get; set; }
        public int IdDestFlete { get; set; }
        public string TipoFlete { get; set; }
        public string TipoViaje { get; set; }
        public int Cantidad { get; set; }
        public string Estatus { get; set; }
        public string Chofer { get; set; }
    }
}
