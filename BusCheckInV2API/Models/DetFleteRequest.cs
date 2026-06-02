namespace BusCheckInV2API.Models
{
    public class DetFleteRequest
    {
        public int IdFletePer { get; set; }
        public int FlePer_CveNomina { get; set; }
        public decimal FlePer_Latitud { get; set; }
        public decimal FlePer_Longitud { get; set; }
        public DateTime FlePer_Fecha { get; set; }
        public string? FlePer_Nombre { get; set; }
    }
}
