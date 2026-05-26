namespace BusCheckInV2API.Models
{
    public class FinalizarFleteRequest
    {
        public int IdFletePer { get; set; }
        public int CantidadReal { get; set; }
        public string Observaciones { get; set; }
        public double Latitud { get; set; }
        public double Longitud { get; set; }
        public string Usuario { get; set; }
    }
}
