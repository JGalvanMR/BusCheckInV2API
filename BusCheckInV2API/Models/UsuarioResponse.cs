namespace BusCheckInV2API.Models
{
    public class UsuarioResponse
    {
        public string Nombre { get; set; }
        public int TotalFletes { get; set; }
        public int FletesPendientes { get; set; }
    }
}
