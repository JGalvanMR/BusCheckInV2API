namespace BusCheckInV2API.Models
{
    public class CancelarFleteRequest
    {
        public int IdFletePer { get; set; }
        public string Motivo { get; set; }
        public string Detalles { get; set; }
        public string Usuario { get; set; }
    }
}
