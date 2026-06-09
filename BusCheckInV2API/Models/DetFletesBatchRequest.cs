namespace BusCheckInV2API.Models
{
    /// <summary>
    /// Payload para sincronización batch de pasajeros escaneados.
    /// Todos los detalles en una sola llamada HTTP.
    /// </summary>
    public class DetFletesBatchRequest
    {
        /// <summary>ID del flete en el servidor (no el local de SQLite).</summary>
        public int IdFletePer { get; set; }

        /// <summary>Lista de pasajeros escaneados offline.</summary>
        public List<DetFleteItem> Items { get; set; } = new();
    }

    public class DetFleteItem
    {
        public int FlePer_CveNomina { get; set; }
        public double FlePer_Latitud { get; set; }
        public double FlePer_Longitud { get; set; }
        public DateTime FlePer_Fecha { get; set; }
        public int LocalId { get; set; }  // Id de Tb_FlePer_DetFlete en SQLite
    }

    /// <summary>
    /// Resultado por item — permite saber cuáles se insertaron y cuáles fallaron.
    /// </summary>
    public class DetFletesBatchResult
    {
        public bool Success { get; set; }
        public int TotalInsertados { get; set; }
        public int TotalFallidos { get; set; }
        public List<int> LocalIdsFallidos { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }
}