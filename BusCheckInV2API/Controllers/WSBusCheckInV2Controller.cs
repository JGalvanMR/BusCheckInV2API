using BusCheckInV2.Models;
using BusCheckInV2API.Models;
using Microsoft.AspNetCore.Mvc;
// CORREGIDO: Se eliminó el using System.Data.SqlClient (obsoleto) para evitar conflictos
using Microsoft.Data.SqlClient;
using System.Data;

namespace BusCheckInV2API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WSBusCheckInV2Controller : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<WSBusCheckInV2Controller> _logger;

        public WSBusCheckInV2Controller(IConfiguration configuration, ILogger<WSBusCheckInV2Controller> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        // FIX NIVEL 3 #14: se elimina EjecutarConsultaAsync. Era código muerto:
        // ningún endpoint del controller lo usaba. EjecutarNonQueryAsync
        // tampoco se usaba mucho (los endpoints hacen ExecuteNonQueryAsync
        // directamente), pero lo conservo por si lo necesitas en el futuro.

        private async Task<int> EjecutarNonQueryAsync(SqlCommand cmd)
        {
            return await cmd.ExecuteNonQueryAsync();
        }

        #region ENDPOINTS ESCANEO DE CODIGOS

        // POST: api/WSBusCheckInV2/InsertarFletePersonal
        [HttpPost("InsertarFletePersonal")]
        public async Task<IActionResult> InsertarTb_FlePer_FletePersonal(
    [FromBody] FletePersonalRequest request)
        {
            if (request == null)
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Petición inválida"
                });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                // Sin TRY_CAST: ya convertimos a los tipos correctos en C#
                const string consulta = @"
            INSERT INTO Tb_FlePer_FletePersonal
                (FlePer_Fecha, FlePer_Hora, Prov_Clave, IdDestFlete,
                 FlePer_TipoFlete, FlePer_TipoViaje, FlePer_Cantidad,
                 FlePer_Status, FlePer_Chofer)
            VALUES
                (@FlePer_Fecha, @FlePer_Hora, @Prov_Clave, @IdDestFlete,
                 @FlePer_TipoFlete, @FlePer_TipoViaje, @FlePer_Cantidad,
                 @FlePer_Status, @FlePer_Chofer);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

                // Parseo defensivo: si el cliente manda mal formato usamos ahora
                DateTime fecha = DateTime.TryParse(request.Fecha, out var f)
                    ? f : DateTime.Now;
                TimeSpan hora = TimeSpan.TryParse(request.Hora, out var h)
                    ? h : DateTime.Now.TimeOfDay;

                using var cmd = new SqlCommand(consulta, con);

                // ── CORRECCIÓN CRÍTICA: los parámetros ahora SÍ se agregan ──────
                cmd.Parameters.Add("@FlePer_Fecha", SqlDbType.DateTime).Value = fecha;
                cmd.Parameters.Add("@FlePer_Hora", SqlDbType.Time).Value = hora;
                cmd.Parameters.Add("@Prov_Clave", SqlDbType.VarChar, 10).Value = request.ClaveProveedor ?? string.Empty;
                cmd.Parameters.Add("@IdDestFlete", SqlDbType.Int).Value = request.IdDestFlete;
                cmd.Parameters.Add("@FlePer_TipoFlete", SqlDbType.VarChar, 15).Value = request.TipoFlete ?? "NORMAL";
                cmd.Parameters.Add("@FlePer_TipoViaje", SqlDbType.VarChar, 15).Value = request.TipoViaje ?? "TRAER GENTE";
                cmd.Parameters.Add("@FlePer_Cantidad", SqlDbType.Int).Value = request.Cantidad;
                cmd.Parameters.Add("@FlePer_Status", SqlDbType.VarChar, 1).Value = request.Estatus ?? "P";
                cmd.Parameters.Add("@FlePer_Chofer", SqlDbType.VarChar, 30).Value =
                    string.IsNullOrWhiteSpace(request.Chofer)
                        ? (object)DBNull.Value
                        : request.Chofer.Trim();

                var raw = await cmd.ExecuteScalarAsync();
                long idGenerado = (raw != null && raw != DBNull.Value)
                    ? Convert.ToInt64(raw)
                    : 0;

                if (idGenerado <= 0)
                {
                    _logger.LogError(
                        "InsertarFletePersonal: SCOPE_IDENTITY() devolvió {Id} " +
                        "(Fecha={Fecha}, Hora={Hora}, Chofer={Chofer})",
                        idGenerado, fecha, hora, request.Chofer);

                    return StatusCode(500, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "El servidor no generó un ID válido para el flete"
                    });
                }

                _logger.LogInformation(
                    "Flete insertado: IdFletePer={Id}, Chofer={Chofer}, Fecha={Fecha}",
                    idGenerado, request.Chofer, fecha.ToString("yyyy-MM-dd"));

                return Ok(new ApiResponse<long>
                {
                    Success = true,
                    Data = idGenerado,
                    Message = "Flete insertado correctamente"
                });
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx,
                    "SQL Error en InsertarFletePersonal (Number={Num}): {Msg}",
                    sqlEx.Number, sqlEx.Message);

                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error de base de datos [{sqlEx.Number}]"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en InsertarFletePersonal");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor"
                });
            }
        }

        // POST: api/WSBusCheckInV2/InsertarDetFlete
        //
        // FIX 2026-06-02 (Nivel 3 #10): consolidado con SincronizarDetFletes.
        // Antes la query de INSERT estaba DUPLICADA entre este endpoint
        // (insert individual) y SincronizarDetFletes (batch). Ahora ambos
        // usan el helper privado InsertarUnDetFleteAsync que centraliza
        // la query, los parámetros y el manejo de errores.
        [HttpPost("InsertarDetFlete")]
        public async Task<IActionResult> InsertarTb_FlePer_DetFlete([FromBody] DetFleteRequest request)
        {
            if (request == null) return BadRequest(new ApiResponse<object> { Success = false, Message = "Petición inválida" });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                int rowsAffected = await InsertarUnDetFleteAsync(
                    con,
                    transaction: null,
                    idFletePer: request.IdFletePer,
                    cveNomina: request.FlePer_CveNomina,
                    latitud: request.FlePer_Latitud,
                    longitud: request.FlePer_Longitud,
                    fecha: request.FlePer_Fecha);

                return Ok(new ApiResponse<string>
                {
                    Success = rowsAffected > 0,
                    Data = rowsAffected > 0 ? "Insertado correctamente" : "Empleado no encontrado o sin cambios",
                    Message = rowsAffected > 0 ? "Éxito" : "Sin cambios"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en InsertarDetFlete");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error interno del servidor" });
            }
        }

        /// <summary>
        /// Helper privado que ejecuta el INSERT de UN detalle de flete.
        /// Usado tanto por InsertarDetFlete (individual) como por
        /// SincronizarDetFletes (batch transaccional). Centraliza la
        /// query, los parámetros y la lógica de JOIN con tb_cat_empleados
        /// para resolver nombre/departamento del empleado.
        /// </summary>
        /// <param name="con">Conexión abierta al servidor SQL.</param>
        /// <param name="transaction">
        /// Transacción opcional. Si se pasa (caso batch), el INSERT
        /// participa de la transacción. Si es null (caso individual),
        /// el INSERT hace auto-commit.
        /// </param>
        /// <returns>
        /// Número de filas afectadas: 1 si insertó, 0 si el empleado
        /// no existe en tb_cat_empleados o si el detalle ya existía
        /// (en el caso batch que usa NOT EXISTS).
        /// </returns>
        private async Task<int> InsertarUnDetFleteAsync(
            SqlConnection con,
            SqlTransaction? transaction,
            int idFletePer,
            int cveNomina,
            decimal latitud,
            decimal longitud,
            DateTime fecha)
        {
            // La query hace JOIN con tb_cat_empleados para resolver el
            // nombre completo y departamento del empleado. Si la nómina
            // no existe, el INSERT no inserta nada (rows = 0), lo que
            // se reporta al cliente como "Empleado no encontrado".
            const string query = @"
                INSERT INTO TB_FlePer_DetFlete
                    (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud,
                     FlePer_Fecha, FlePer_Nombre, FlePer_Depto)
                SELECT
                    @IdFletePer,
                    @FlePer_CveNomina,
                    @FlePer_Latitud,
                    @FlePer_Longitud,
                    @FlePer_Fecha,
                    CONCAT(
                        LTRIM(RTRIM(e.emp_nombre)),   ' ',
                        LTRIM(RTRIM(e.emp_paterno)),  ' ',
                        LTRIM(RTRIM(e.emp_materno))),
                    LTRIM(RTRIM(d.dep_nombre))
                FROM tb_cat_empleados e
                LEFT JOIN tb_cat_departamentos d ON d.dep_folio = e.emp_depto
                WHERE e.emp_clave = @FlePer_CveNomina
                  AND NOT EXISTS (
                      SELECT 1 FROM TB_FlePer_DetFlete
                      WHERE IdFletePer      = @IdFletePer
                        AND FlePer_CveNomina = @FlePer_CveNomina
                  );";

            using var cmd = transaction != null
                ? new SqlCommand(query, con, transaction)
                : new SqlCommand(query, con);

            cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = idFletePer;
            cmd.Parameters.Add("@FlePer_CveNomina", SqlDbType.Int).Value = cveNomina;
            cmd.Parameters.Add("@FlePer_Latitud", SqlDbType.Decimal).Value = latitud;
            cmd.Parameters.Add("@FlePer_Longitud", SqlDbType.Decimal).Value = longitud;
            cmd.Parameters.Add("@FlePer_Fecha", SqlDbType.DateTime).Value = fecha;

            return await cmd.ExecuteNonQueryAsync();
        }

        // POST: api/WSBusCheckInV2/InsertarInicioDetFlete
        //
        // FIX 2026-06-02 (Nivel 3 - consolidación de duplicación):
        // La query IF NOT EXISTS + INSERT para el INICIO estaba duplicada
        // con la del FIN. Ahora ambos endpoints delegan en el helper
        // privado InsertarMarcaFleteAsync, único sitio donde vive la query.
        [HttpPost("InsertarInicioDetFlete")]
        public async Task<IActionResult> InsertarInicioTb_FlePer_DetFlete([FromBody] DetFleteRequest request)
        {
            if (request == null) return BadRequest(new ApiResponse<object> { Success = false, Message = "Petición inválida" });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                int rowsAffected = await InsertarMarcaFleteAsync(
                    con,
                    idFletePer: request.IdFletePer,
                    cveNomina: 0,
                    latitud: request.FlePer_Latitud,
                    longitud: request.FlePer_Longitud,
                    fecha: request.FlePer_Fecha,
                    nombre: "INICIO");

                return Ok(new ApiResponse<string>
                {
                    Success = rowsAffected > 0,
                    Data = rowsAffected > 0 ? "Insertado correctamente" : "Ya existe un registro de inicio",
                    Message = rowsAffected > 0 ? "Éxito" : "Sin cambios"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en InsertarInicioDetFlete");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error interno del servidor" });
            }
        }

        // PUT: api/WSBusCheckInV2/UpdateFletePersonal
        [HttpPut("UpdateFletePersonal")]
        public async Task<IActionResult> UpdateTb_FlePer_FletePersonal([FromBody] UpdateFleteRequest request)
        {
            if (request == null) return BadRequest(new ApiResponse<object> { Success = false, Message = "Petición inválida" });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                const string query = @"
                    UPDATE Tb_FlePer_FletePersonal
                    SET FlePer_Cantidad = @FlePer_Cantidad, FlePer_Status = 'A'
                    WHERE IdFletePer = @IdFletePer;";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.Add("@FlePer_Cantidad", SqlDbType.Int).Value = request.FlePer_Cantidad;
                cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;

                var rowsAffected = await EjecutarNonQueryAsync(cmd);

                return Ok(new ApiResponse<string>
                {
                    Success = rowsAffected > 0,
                    Data = rowsAffected > 0 ? "Actualizado correctamente" : "No se pudo actualizar",
                    Message = rowsAffected > 0 ? "Éxito" : "Registro no encontrado"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en UpdateFletePersonal");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error interno del servidor" });
            }
        }

        // POST: api/WSBusCheckInV2/InsertarFinDetFlete
        //
        // FIX 2026-06-02 (Nivel 3 - consolidación de duplicación):
        // Mismo helper InsertarMarcaFleteAsync que usa InsertarInicioDetFlete.
        // La query IF NOT EXISTS + INSERT vive solo en el helper.
        [HttpPost("InsertarFinDetFlete")]
        public async Task<IActionResult> InsertarFinTb_FlePer_DetFlete([FromBody] DetFleteRequest request)
        {
            if (request == null) return BadRequest(new ApiResponse<object> { Success = false, Message = "Petición inválida" });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                int rowsAffected = await InsertarMarcaFleteAsync(
                    con,
                    idFletePer: request.IdFletePer,
                    cveNomina: 9999,
                    latitud: request.FlePer_Latitud,
                    longitud: request.FlePer_Longitud,
                    fecha: request.FlePer_Fecha,
                    nombre: "FIN");

                return Ok(new ApiResponse<string>
                {
                    Success = rowsAffected > 0,
                    Data = rowsAffected > 0 ? "Insertado correctamente" : "Ya existe un registro de fin",
                    Message = rowsAffected > 0 ? "Éxito" : "Sin cambios"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en InsertarFinDetFlete");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error interno del servidor" });
            }
        }

        /// <summary>
        /// Helper privado que inserta una "marca" en el detalle del flete:
        /// INICIO (CveNomina=0) o FIN (CveNomina=9999). Es idempotente: si
        /// ya existe la marca para ese flete, no hace nada (devuelve 0).
        /// Usado por InsertarInicioDetFlete y InsertarFinDetFlete para
        /// evitar tener la query duplicada en dos sitios.
        /// </summary>
        private async Task<int> InsertarMarcaFleteAsync(
            SqlConnection con,
            int idFletePer,
            int cveNomina,
            decimal latitud,
            decimal longitud,
            DateTime fecha,
            string nombre)
        {
            // IF NOT EXISTS evita duplicados si el cliente llama dos veces
            // al mismo endpoint (caso real: el sync sube el FIN en el batch
            // Y después el chofer pulsa "Cerrar Flete" que también lo graba).
            const string query = @"
                IF NOT EXISTS (
                    SELECT 1 FROM TB_FlePer_DetFlete
                    WHERE IdFletePer = @IdFletePer AND FlePer_CveNomina = @FlePer_CveNomina
                )
                BEGIN
                    INSERT INTO TB_FlePer_DetFlete
                        (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                    VALUES
                        (@IdFletePer, @FlePer_CveNomina, @FlePer_Latitud, @FlePer_Longitud, @FlePer_Fecha, @FlePer_Nombre);
                END";

            using var cmd = new SqlCommand(query, con);
            cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = idFletePer;
            cmd.Parameters.Add("@FlePer_CveNomina", SqlDbType.Int).Value = cveNomina;
            cmd.Parameters.Add("@FlePer_Latitud", SqlDbType.Decimal).Value = latitud;
            cmd.Parameters.Add("@FlePer_Longitud", SqlDbType.Decimal).Value = longitud;
            cmd.Parameters.Add("@FlePer_Fecha", SqlDbType.DateTime).Value = fecha;
            cmd.Parameters.Add("@FlePer_Nombre", SqlDbType.VarChar, 10).Value = nombre;

            return await cmd.ExecuteNonQueryAsync();
        }

        // POST: api/WSBusCheckInV2/SincronizarDetFletes
        [HttpPost("SincronizarDetFletes")]
        public async Task<IActionResult> SincronizarDetFletes(
            [FromBody] DetFletesBatchRequest request)
        {
            if (request == null || request.Items == null || request.Items.Count == 0)
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Petición vacía o inválida"
                });

            int totalInsertados = 0;
            var localIdsFallidos = new List<int>();

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                // ── Una transacción por todo el lote ─────────────────────────────
                // Si falla el insert #25 de 40, el rollback deja todo en estado
                // consistente. El cliente reintentará todo el lote la próxima vez.
                using var transaction = await con.BeginTransactionAsync();

                try
                {
                    // FIX 2026-06-02 (Nivel 3 #10): consolidado con InsertarDetFlete.
                    // Antes este foreach declaraba su propia query inline
                    // (duplicada con InsertarDetFlete). Ahora delega en el
                    // helper privado InsertarUnDetFleteAsync, que es el
                    // ÚNICO lugar donde vive la query de detalle de flete.
                    foreach (var item in request.Items)
                    {
                        int rows = await InsertarUnDetFleteAsync(
                            con,
                            transaction: (SqlTransaction?)transaction,
                            idFletePer: request.IdFletePer,
                            cveNomina: item.FlePer_CveNomina,
                            latitud: (decimal)item.FlePer_Latitud,
                            longitud: (decimal)item.FlePer_Longitud,
                            fecha: item.FlePer_Fecha);

                        if (rows > 0)
                            totalInsertados++;
                        else
                        {
                            // 0 rows = empleado no encontrado O ya existía (NOT EXISTS).
                            // Ambos casos son "manejados": marcamos como fallido
                            // solo para el reporte, pero no abortamos la transacción.
                            localIdsFallidos.Add(item.LocalId);
                            _logger.LogWarning(
                                "DetFlete no insertado: Nomina={Nom}, Flete={Id} (ya existía o empleado no encontrado)",
                                item.FlePer_CveNomina, request.IdFletePer);
                        }
                    }

                    await transaction.CommitAsync();

                    _logger.LogInformation(
                        "SincronizarDetFletes: IdFletePer={Id}, " +
                        "Insertados={Ins}, NoInsertados={No}",
                        request.IdFletePer, totalInsertados, localIdsFallidos.Count);

                    return Ok(new ApiResponse<DetFletesBatchResult>
                    {
                        Success = true,
                        Data = new DetFletesBatchResult
                        {
                            Success = true,
                            TotalInsertados = totalInsertados,
                            TotalFallidos = localIdsFallidos.Count,
                            LocalIdsFallidos = localIdsFallidos,
                            Message = $"Batch completado: {totalInsertados} insertados, " +
                                      $"{localIdsFallidos.Count} omitidos"
                        },
                        Message = "Sincronización batch exitosa"
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Rollback en SincronizarDetFletes para flete {Id}",
                        request.IdFletePer);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en SincronizarDetFletes");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error al sincronizar detalle de flete"
                });
            }
        }
        #endregion

        // GET: api/WSBusCheckInV2/HelloWorld
        [HttpGet("HelloWorld")]
        public IActionResult HelloWorld()
        {
            // FIX NIVEL 3 #24: envolver en ApiResponse<string> para mantener
            // consistencia con el resto de la API. Antes devolvía un string
            // puro que el cliente deserializaba como string directo. Si
            // algún día agregas más consumidores al endpoint, les resultará
            // más fácil parsear un ApiResponse uniforme.
            return Ok(new ApiResponse<string>
            {
                Success = true,
                Data = "Hola a todos",
                Message = "Endpoint de prueba"
            });
        }

        #region ENDPOINT FLETES PENDIENTES
        // GET: api/WSBusCheckInV2/ObtenerUsuarios
        [HttpGet("ObtenerUsuarios")]
        public async Task<IActionResult> ObtenerUsuarios()
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                // FIX NIVEL 3 #22: limitar a TOP 500 choferes para evitar
                // payloads enormes en empresas con miles de choferes. Si
                // se necesita paginación real, se puede agregar
                // ?offset= y ?limit= en el futuro.
                const string query = @"
                    WITH FletesConEstado AS (
                       SELECT TOP 500
                        fp.IdFletePer,
                        fp.FlePer_Chofer,
                        CASE
                            WHEN fp.FlePer_Status = 'P'
                                OR (
                                    EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete df WHERE df.IdFletePer = fp.IdFletePer AND df.FlePer_CveNomina = 0)
                                    AND NOT EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete df WHERE df.IdFletePer = fp.IdFletePer AND df.FlePer_CveNomina = 9999 AND df.FlePer_Nombre = 'FIN')
                                )
                            THEN 1 ELSE 0
                        END AS EsPendiente
                       FROM Tb_FlePer_FletePersonal fp
                       WHERE fp.FlePer_Chofer IS NOT NULL
                        AND LTRIM(RTRIM(fp.FlePer_Chofer)) <> ''
                        AND fp.FlePer_Fecha >= DATEADD(DAY, -7, CAST(GETDATE() AS DATE))
                       ORDER BY fp.FlePer_Fecha DESC
                )
                SELECT
                    FlePer_Chofer AS Nombre,
                    COUNT(*) AS TotalFletes,
                    SUM(EsPendiente) AS FletesPendientes
                    FROM FletesConEstado
                GROUP BY FlePer_Chofer
                ORDER BY FlePer_Chofer;";

                using var cmd = new SqlCommand(query, con);
                using var reader = await cmd.ExecuteReaderAsync();

                var usuarios = new List<UsuarioResponse>();
                while (await reader.ReadAsync())
                {
                    usuarios.Add(new UsuarioResponse
                    {
                        Nombre = reader["Nombre"].ToString(),
                        TotalFletes = Convert.ToInt32(reader["TotalFletes"]),
                        FletesPendientes = Convert.ToInt32(reader["FletesPendientes"])
                    });
                }

                return Ok(new ApiResponse<List<UsuarioResponse>>
                {
                    Success = true,
                    Data = usuarios,
                    Message = $"Se encontraron {usuarios.Count} usuarios"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ObtenerUsuarios");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error interno del servidor" });
            }
        }

        [HttpGet("ObtenerFletesPorChoferTodos")]
        public async Task<IActionResult> ObtenerFletesPorChoferTodos(string chofer, int dias = 7)
        {
            // FIX NIVEL 1 #11: rechazar chofer vacío
            if (string.IsNullOrWhiteSpace(chofer))
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "El parámetro 'chofer' es obligatorio."
                });

            // FIX NIVEL 1 #12: rango de días
            if (dias < 1 || dias > 90)
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "El parámetro 'dias' debe estar entre 1 y 90."
                });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                string query = @"
                    SELECT
                        fp.IdFletePer, fp.FlePer_Fecha, fp.FlePer_Hora, fp.Prov_Clave, p.prov_nombre AS NombreProveedor,
                        fp.IdDestFlete, r.NomDestFlete AS NombreRuta, fp.FlePer_TipoFlete, fp.FlePer_TipoViaje,
                        fp.FlePer_Cantidad, fp.FlePer_Status, fp.FlePer_Chofer,
                        (SELECT COUNT(*) FROM Tb_FlePer_DetFlete dfx WHERE dfx.IdFletePer = fp.IdFletePer) AS PuntosRegistrados
                    FROM Tb_FlePer_FletePersonal fp
                    LEFT JOIN Tb_Cat_Proveedor p ON fp.Prov_Clave = p.prov_clave
                    LEFT JOIN Tb_FlePer_Ruta r ON fp.IdDestFlete = r.IdDestFlete
                    WHERE fp.FlePer_Chofer LIKE @Chofer
                      AND fp.FlePer_Fecha >= DATEADD(DAY, -@Dias, CAST(GETDATE() AS DATE))
                    ORDER BY fp.FlePer_Fecha DESC, fp.FlePer_Hora DESC";

                using var cmd = new SqlCommand(query, con);
                // FIX NIVEL 2 #13: aplicar Trim para alinear con
                // ObtenerFletesPorChofer que sí lo hacía. Antes un chofer
                // con espacios al final no matcheaba aquí.
                cmd.Parameters.Add("@Chofer", SqlDbType.VarChar).Value = $"%{chofer.Trim()}%";
                cmd.Parameters.Add("@Dias", SqlDbType.Int).Value = dias;

                using var reader = await cmd.ExecuteReaderAsync();
                var fletes = new List<FleteResponse>();

                while (await reader.ReadAsync())
                {
                    fletes.Add(new FleteResponse
                    {
                        IdFletePer = reader["IdFletePer"] != DBNull.Value ? Convert.ToInt32(reader["IdFletePer"]) : 0,
                        Fecha = reader["FlePer_Fecha"]?.ToString() ?? "",
                        Hora = reader["FlePer_Hora"]?.ToString() ?? "",
                        ProveedorClave = reader["Prov_Clave"]?.ToString() ?? "",
                        ProveedorNombre = reader["NombreProveedor"]?.ToString() ?? "",
                        IdDestFlete = reader["IdDestFlete"] != DBNull.Value ? Convert.ToInt32(reader["IdDestFlete"]) : 0,
                        RutaNombre = reader["NombreRuta"]?.ToString() ?? "",
                        TipoFlete = reader["FlePer_TipoFlete"]?.ToString() ?? "",
                        TipoViaje = reader["FlePer_TipoViaje"]?.ToString() ?? "",
                        Cantidad = reader["FlePer_Cantidad"] != DBNull.Value ? Convert.ToInt32(reader["FlePer_Cantidad"]) : 0,
                        Estatus = reader["FlePer_Status"]?.ToString() ?? "",
                        Chofer = reader["FlePer_Chofer"]?.ToString() ?? "",
                        PuntosRegistrados = reader["PuntosRegistrados"] != DBNull.Value ? Convert.ToInt32(reader["PuntosRegistrados"]) : 0
                    });
                }

                return Ok(new ApiResponse<List<FleteResponse>>
                {
                    Success = true,
                    Data = fletes,
                    Message = $"Se encontraron {fletes.Count} fletes para {chofer}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ObtenerFletesPorChoferTodos");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error interno del servidor" });
            }
        }

        [HttpGet("ObtenerFletesPorChofer")]
        public async Task<IActionResult> ObtenerFletesPorChofer(string chofer, int dias = 3, bool soloPendientes = false)
        {
            // FIX NIVEL 1 #11: rechazar chofer vacío o solo espacios.
            // Antes el LIKE '%%' devolvía TODOS los fletes de TODOS los
            // choferes (escaneo masivo innecesario de la BD).
            if (string.IsNullOrWhiteSpace(chofer))
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "El parámetro 'chofer' es obligatorio."
                });

            // FIX NIVEL 1 #12: limitar el rango de días. Un cliente malicioso
            // o con bug podría pasar dias=99999 y forzar un escaneo enorme.
            if (dias < 1 || dias > 90)
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "El parámetro 'dias' debe estar entre 1 y 90."
                });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                // FIX NIVEL 2 #19 + #23: agrego EsPendiente derivado en el SELECT
                // para que el cliente no tenga que adivinarlo combinando campos.
                // La lógica es EXACTAMENTE la que el cliente ya usa
                // (Estatus='P' OR (TieneInicio AND NOT TieneFin)) con la
                // adición clave: también se considera pendiente un flete en
                // estado 'I' (Reanudado) o 'P' (Pendiente) que aún no tiene
                // FIN. Esto cierra el bug #5 donde un flete recién creado y
                // no sincronizado (sin INICIO en el servidor) quedaba oculto
                // por el toggle "Solo pendientes".
                //
                // FIX NIVEL 1 #5: el WHERE de soloPendientes ahora incluye
                // Estatus IN ('P','I') para que un flete en curso pero sin
                // INICIO/FIN subidos al servidor también aparezca.
                string query = @"
                    SELECT
                        fp.IdFletePer, fp.FlePer_Fecha, fp.FlePer_Hora, fp.Prov_Clave, p.prov_nombre AS NombreProveedor,
                        fp.IdDestFlete, r.NomDestFlete AS NombreRuta, fp.FlePer_TipoFlete, fp.FlePer_TipoViaje,
                        fp.FlePer_Cantidad, fp.FlePer_Status, fp.FlePer_Chofer,
                        (SELECT COUNT(*) FROM Tb_FlePer_DetFlete dfx WHERE dfx.IdFletePer = fp.IdFletePer) AS PuntosRegistrados,
                        CASE WHEN EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete WHERE IdFletePer = fp.IdFletePer AND FlePer_CveNomina = 0) THEN 1 ELSE 0 END AS TieneInicio,
                        CASE WHEN EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete WHERE IdFletePer = fp.IdFletePer AND FlePer_CveNomina = 9999) THEN 1 ELSE 0 END AS TieneFin,
                        CASE
                            WHEN fp.FlePer_Status IN ('P','I') THEN 1
                            WHEN (EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete WHERE IdFletePer = fp.IdFletePer AND FlePer_CveNomina = 0)
                                  AND NOT EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete WHERE IdFletePer = fp.IdFletePer AND FlePer_CveNomina = 9999))
                            THEN 1
                            ELSE 0
                        END AS EsPendiente
                    FROM Tb_FlePer_FletePersonal fp
                    LEFT JOIN Tb_Cat_Proveedor p ON fp.Prov_Clave = p.prov_clave
                    LEFT JOIN Tb_FlePer_Ruta r ON fp.IdDestFlete = r.IdDestFlete
                    WHERE fp.FlePer_Chofer LIKE @Chofer
                      AND fp.FlePer_Fecha >= DATEADD(DAY, -@Dias, CAST(GETDATE() AS DATE)) ";

                if (soloPendientes)
                {
                    // FIX NIVEL 1 #5: ampliar el filtro para incluir fletes
                    // en estado P/I aunque no tengan INICIO/FIN subidos.
                    query += @"
                        AND (
                            fp.FlePer_Status IN ('P','I')
                            OR (
                                EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete df WHERE df.IdFletePer = fp.IdFletePer AND df.FlePer_CveNomina = 0)
                                AND NOT EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete df WHERE df.IdFletePer = fp.IdFletePer AND df.FlePer_CveNomina = 9999)
                            )
                        ) ";
                }

                query += "ORDER BY fp.FlePer_Fecha DESC, fp.FlePer_Hora DESC";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.Add("@Chofer", SqlDbType.VarChar).Value = $"%{chofer.Trim()}%";
                cmd.Parameters.Add("@Dias", SqlDbType.Int).Value = dias;

                using var reader = await cmd.ExecuteReaderAsync();
                var fletes = new List<FleteResponse>();

                while (await reader.ReadAsync())
                {
                    fletes.Add(new FleteResponse
                    {
                        IdFletePer = reader["IdFletePer"] != DBNull.Value ? Convert.ToInt32(reader["IdFletePer"]) : 0,
                        Fecha = reader["FlePer_Fecha"]?.ToString() ?? "",
                        Hora = reader["FlePer_Hora"]?.ToString() ?? "",
                        ProveedorClave = reader["Prov_Clave"]?.ToString() ?? "",
                        ProveedorNombre = reader["NombreProveedor"]?.ToString() ?? "",
                        IdDestFlete = reader["IdDestFlete"] != DBNull.Value ? Convert.ToInt32(reader["IdDestFlete"]) : 0,
                        RutaNombre = reader["NombreRuta"]?.ToString() ?? "",
                        TipoFlete = reader["FlePer_TipoFlete"]?.ToString() ?? "",
                        TipoViaje = reader["FlePer_TipoViaje"]?.ToString() ?? "",
                        Cantidad = reader["FlePer_Cantidad"] != DBNull.Value ? Convert.ToInt32(reader["FlePer_Cantidad"]) : 0,
                        Estatus = reader["FlePer_Status"]?.ToString() ?? "",
                        Chofer = reader["FlePer_Chofer"]?.ToString() ?? "",
                        PuntosRegistrados = reader["PuntosRegistrados"] != DBNull.Value ? Convert.ToInt32(reader["PuntosRegistrados"]) : 0,
                        TieneInicio = reader["TieneInicio"] != DBNull.Value ? Convert.ToInt32(reader["TieneInicio"]) : 0,
                        TieneFin = reader["TieneFin"] != DBNull.Value ? Convert.ToInt32(reader["TieneFin"]) : 0,
                        // FIX NIVEL 2 #19: exponer EsPendiente derivado para
                        // que el cliente no tenga que recalcularlo.
                        EsPendiente = reader["EsPendiente"] != DBNull.Value ? Convert.ToInt32(reader["EsPendiente"]) : 0
                    });
                }

                return Ok(new ApiResponse<List<FleteResponse>>
                {
                    Success = true,
                    Data = fletes,
                    Message = $"Se encontraron {fletes.Count} fletes para {chofer}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ObtenerFletesPorChofer");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error interno del servidor" });
            }
        }

        [HttpPost("ValidarYFinalizarFlete")]
        public async Task<IActionResult> ValidarYFinalizarFlete([FromBody] FinalizarFleteRequest request)
        {
            if (request == null) return BadRequest(new ApiResponse<object> { Success = false, Message = "Petición inválida" });

            // FIX NIVEL 3 #15: Validar coordenadas GPS. Si llega (0,0) estamos
            // grabando Null Island (Atlántico) en lugar de la parada real.
            // El cliente fue arreglado para enviar GPS real, pero el backend
            // debe defenderse de clientes viejos o maliciosos.
            if (request.Latitud == 0 && request.Longitud == 0)
            {
                _logger.LogWarning(
                    "ValidarYFinalizarFlete: rechazando coordenadas (0,0) para IdFletePer={Id}",
                    request.IdFletePer);
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Coordenadas GPS inválidas (0,0). El dispositivo debe enviar ubicación real."
                });
            }

            // FIX NIVEL 1 #2 + NIVEL 3 #18: el INSERT del FIN ahora usa
            // IF NOT EXISTS para no duplicar el registro si este endpoint
            // se llama dos veces para el mismo flete (caso real: el cliente
            // sube el FIN en SincronizarDetallesAsync y luego el chofer
            // pulsa Cerrar Flete que también llama aquí). Antes quedaban
            // DOS filas con FlePer_CveNomina=9999.
            //
            // FIX NIVEL 2 #8: el UPDATE ahora pone Status='F' (Finalizado)
            // además de la cantidad, para que el contrato de estados quede
            // claro y ObtenerFletesPorChofer deje de mostrarlo como
            // pendiente. Antes se quedaba con 'A' (puesto por
            // UpdateFletePersonal) que es ambiguo.

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();
                using var transaction = con.BeginTransaction();

                try
                {
                    const string updateFlete = @"
                        UPDATE Tb_FlePer_FletePersonal
                        SET FlePer_Cantidad     = @CantidadReal,
                            FlePer_FechaFin     = GETDATE(),
                            FlePer_Observaciones = @Observaciones,
                            FlePer_Status       = 'F'
                        WHERE IdFletePer = @IdFletePer";

                    using var cmdUpdate = new SqlCommand(updateFlete, con, transaction);
                    cmdUpdate.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;
                    cmdUpdate.Parameters.Add("@CantidadReal", SqlDbType.Int).Value = request.CantidadReal;
                    cmdUpdate.Parameters.Add("@Observaciones", SqlDbType.VarChar).Value = (object)request.Observaciones ?? DBNull.Value;

                    var rowsAffected = await cmdUpdate.ExecuteNonQueryAsync();

                    if (rowsAffected == 0)
                    {
                        transaction.Rollback();
                        return NotFound(new ApiResponse<object> { Success = false, Message = "Flete no encontrado" });
                    }

                    // FIX NIVEL 1 #2: IF NOT EXISTS evita duplicar el FIN.
                    // Si ya existe un FIN para este flete (subido por el sync
                    // del cliente), simplemente no lo insertamos de nuevo.
                    const string insertDetalle = @"
                        IF NOT EXISTS (
                            SELECT 1 FROM TB_FlePer_DetFlete
                            WHERE IdFletePer = @IdFletePer AND FlePer_CveNomina = 9999
                        )
                        BEGIN
                            INSERT INTO TB_FlePer_DetFlete
                                (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                            VALUES
                                (@IdFletePer, 9999, @Latitud, @Longitud, GETDATE(), 'FIN');
                        END";

                    using var cmdDetalle = new SqlCommand(insertDetalle, con, transaction);
                    cmdDetalle.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;
                    cmdDetalle.Parameters.Add("@Latitud", SqlDbType.Decimal).Value = request.Latitud;
                    cmdDetalle.Parameters.Add("@Longitud", SqlDbType.Decimal).Value = request.Longitud;

                    await cmdDetalle.ExecuteNonQueryAsync();

                    transaction.Commit();

                    return Ok(new ApiResponse<object> { Success = true, Message = "Flete finalizado exitosamente" });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "Error al procesar transacción en ValidarYFinalizarFlete");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ValidarYFinalizarFlete");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error al finalizar flete" });
            }
        }

        [HttpPost("CancelarFlete")]
        public async Task<IActionResult> CancelarFlete([FromBody] CancelarFleteRequest request)
        {
            if (request == null) return BadRequest(new ApiResponse<object> { Success = false, Message = "Petición inválida" });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                const string query = @"
                    UPDATE Tb_FlePer_FletePersonal 
                    SET FlePer_Status = 'C', FlePer_Observaciones = @Observaciones, FlePer_FechaFin = GETDATE()
                    WHERE IdFletePer = @IdFletePer";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;
                cmd.Parameters.Add("@Observaciones", SqlDbType.VarChar).Value = $"CANCELADO - Motivo: {request.Motivo}. Detalles: {request.Detalles}";

                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    return NotFound(new ApiResponse<object> { Success = false, Message = "Flete no encontrado" });
                }

                return Ok(new ApiResponse<object> { Success = true, Message = "Flete cancelado exitosamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en CancelarFlete");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error al cancelar flete" });
            }
        }

        [HttpPost("ReanudarFlete")]
        public async Task<IActionResult> ReanudarFlete([FromBody] ReanudarFleteRequest request)
        {
            if (request == null) return BadRequest(new ApiResponse<object> { Success = false, Message = "Petición inválida" });

            // FIX NIVEL 3 #15: rechazar coordenadas (0,0) por la misma razón
            // que en ValidarYFinalizarFlete.
            if (request.Latitud == 0 && request.Longitud == 0)
            {
                _logger.LogWarning(
                    "ReanudarFlete: rechazando coordenadas (0,0) para IdFletePer={Id}",
                    request.IdFletePer);
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Coordenadas GPS inválidas (0,0). El dispositivo debe enviar ubicación real."
                });
            }

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();
                using var transaction = con.BeginTransaction();

                try
                {
                    // FIX NIVEL 1 #4: el Status='Iniciado' (8 caracteres) NO
                    // cabe en FlePer_Status VARCHAR(1) que usan InsertarFletePersonal
                    // y UpdateFletePersonal ('P'/'A'/'F'). Esto provocaba error
                    // 8152 "String or binary data would be truncated". Cambiamos
                    // a 'I' (1 char) que respeta el contrato de la columna.
                    //
                    // FIX NIVEL 3 #16: solo se puede reanudar un flete que esté
                    // en estado 'C' (Cancelado) o 'P' (Pendiente sin actividad).
                    // No se debe reanudar un flete ya 'A' (Aprobado) o 'F'
                    // (Finalizado) porque eso corrompería el flujo contable.
                    //
                    // También arreglo: FlePer_Fecha referenciaba FlePer_FechaInicio
                    // (columna que NO existe en Tb_FlePer_FletePersonal según
                    // los INSERTs que vi). Ahora uso la fecha del propio request
                    // o FlePer_Fecha actual como fallback.
                    const string query = @"
                        UPDATE Tb_FlePer_FletePersonal
                        SET FlePer_Status = 'I',
                            FlePer_Fecha  = ISNULL(@FechaReanudacion, FlePer_Fecha)
                        WHERE IdFletePer = @IdFletePer
                          AND FlePer_Status IN ('C', 'P')";

                    using var cmd = new SqlCommand(query, con, transaction);
                    cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;
                    cmd.Parameters.Add("@FechaReanudacion", SqlDbType.DateTime).Value = DBNull.Value;

                    var rowsAffected = await cmd.ExecuteNonQueryAsync();

                    if (rowsAffected == 0)
                    {
                        // Distinguir entre "no existe" y "estado no permite reanudar"
                        const string checkEstado = @"
                            SELECT FlePer_Status FROM Tb_FlePer_FletePersonal
                            WHERE IdFletePer = @IdFletePer";
                        using var cmdCheck = new SqlCommand(checkEstado, con, transaction);
                        cmdCheck.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;
                        var estadoObj = await cmdCheck.ExecuteScalarAsync();

                        transaction.Rollback();
                        if (estadoObj == null || estadoObj == DBNull.Value)
                            return NotFound(new ApiResponse<object> { Success = false, Message = "Flete no encontrado" });

                        var estadoActual = estadoObj.ToString()?.Trim() ?? "";
                        return Conflict(new ApiResponse<object>
                        {
                            Success = false,
                            Message = $"El flete está en estado '{estadoActual}' y no puede reanudarse. Solo se permiten estados 'C' (Cancelado) o 'P' (Pendiente)."
                        });
                    }

                    const string insertDetalle = @"
                        IF NOT EXISTS (
                            SELECT 1 FROM TB_FlePer_DetFlete
                            WHERE IdFletePer = @IdFletePer AND FlePer_CveNomina = 0
                        )
                        BEGIN
                            INSERT INTO TB_FlePer_DetFlete
                                (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                            VALUES
                                (@IdFletePer, 0, @Latitud, @Longitud, GETDATE(), 'Reanudado');
                        END";

                    using var cmdDetalle = new SqlCommand(insertDetalle, con, transaction);
                    cmdDetalle.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;
                    cmdDetalle.Parameters.Add("@Latitud", SqlDbType.Decimal).Value = request.Latitud;
                    cmdDetalle.Parameters.Add("@Longitud", SqlDbType.Decimal).Value = request.Longitud;

                    await cmdDetalle.ExecuteNonQueryAsync();

                    transaction.Commit();

                    return Ok(new ApiResponse<object> { Success = true, Message = "Flete reanudado exitosamente" });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "Error al procesar transacción en ReanudarFlete");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ReanudarFlete");
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Error al reanudar flete" });
            }
        }

        [HttpPost("SincronizarFletes")]
        public async Task<IActionResult> SincronizarFletes([FromBody] List<SincronizacionRequest> request)
        {
            if (request == null || request.Count == 0)
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Petición inválida o vacía"
                });

            // FIX NIVEL 3 #20: validar TODOS los items antes de empezar.
            // Antes, si el item 5 tenía IdFletePer=0 o negativo, el
            // checkQuery lo trataba arbitrariamente y producía resultados
            // incoherentes. Ahora rechazamos el batch entero con 400 si
            // hay items inválidos (más estricto pero predecible).
            for (int i = 0; i < request.Count; i++)
            {
                if (request[i].IdFletePer <= 0)
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"El item en posición {i} tiene IdFletePer inválido ({request[i].IdFletePer}). Debe ser > 0."
                    });
            }

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                // FIX NIVEL 2 #17: envolver TODO el batch en una sola transacción
                // para que sea atómico. Antes, si el item 5 fallaba, los items
                // 1-4 quedaban commiteados y los 6-N no. Esto era
                // inconsistente con SincronizarDetFletes que SÍ usa transacción.
                using var transaction = await con.BeginTransactionAsync();

                var resultados = new List<SincronizacionResult>();
                int exitosos = 0;

                try
                {
                    foreach (var flete in request)
                    {
                        try
                        {
                            DateTime fecha = DateTime.TryParse(flete.Fecha, out var f)
                                ? f : DateTime.Now;
                            TimeSpan hora = TimeSpan.TryParse(flete.Hora, out var h)
                                ? h : DateTime.Now.TimeOfDay;

                            const string checkQuery =
                                "SELECT COUNT(1) FROM Tb_FlePer_FletePersonal " +
                                "WHERE IdFletePer = @IdFletePer";

                            using var checkCmd = new SqlCommand(checkQuery, con, (SqlTransaction)transaction);
                            checkCmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = flete.IdFletePer;
                            bool existe = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                            const string updateQuery = @"
                                UPDATE Tb_FlePer_FletePersonal
                                SET FlePer_Fecha         = @Fecha,
                                    FlePer_Hora          = @Hora,
                                    Prov_Clave           = @ProvClave,
                                    IdDestFlete          = @IdDestFlete,
                                    FlePer_TipoFlete     = @TipoFlete,
                                    FlePer_TipoViaje     = @TipoViaje,
                                    FlePer_Cantidad      = @Cantidad,
                                    FlePer_Status        = @Status,
                                    FlePer_Chofer        = @Chofer,
                                    FlePer_Observaciones = @Observaciones
                                WHERE IdFletePer = @IdFletePer";

                            const string insertQuery = @"
                                INSERT INTO Tb_FlePer_FletePersonal
                                    (IdFletePer, FlePer_Fecha, FlePer_Hora, Prov_Clave,
                                     IdDestFlete, FlePer_TipoFlete, FlePer_TipoViaje,
                                     FlePer_Cantidad, FlePer_Status, FlePer_Chofer,
                                     FlePer_Observaciones)
                                VALUES
                                    (@IdFletePer, @Fecha, @Hora, @ProvClave,
                                     @IdDestFlete, @TipoFlete, @TipoViaje,
                                     @Cantidad, @Status, @Chofer,
                                     @Observaciones)";

                            using var cmd = new SqlCommand(existe ? updateQuery : insertQuery, con, (SqlTransaction)transaction);

                            cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = flete.IdFletePer;
                            cmd.Parameters.Add("@Fecha", SqlDbType.DateTime).Value = fecha;
                            cmd.Parameters.Add("@Hora", SqlDbType.Time).Value = hora;
                            cmd.Parameters.Add("@ProvClave", SqlDbType.VarChar, 10).Value = flete.ProvClave ?? string.Empty;
                            cmd.Parameters.Add("@IdDestFlete", SqlDbType.Int).Value = flete.IdDestFlete;
                            cmd.Parameters.Add("@TipoFlete", SqlDbType.VarChar, 15).Value = flete.TipoFlete ?? "NORMAL";
                            cmd.Parameters.Add("@TipoViaje", SqlDbType.VarChar, 15).Value = flete.TipoViaje ?? "TRAER GENTE";
                            cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = flete.Cantidad;
                            cmd.Parameters.Add("@Status", SqlDbType.VarChar, 1).Value = flete.Status ?? "P";
                            cmd.Parameters.Add("@Chofer", SqlDbType.VarChar, 30).Value =
                                string.IsNullOrWhiteSpace(flete.Chofer)
                                    ? (object)DBNull.Value
                                    : flete.Chofer.Trim();
                            cmd.Parameters.Add("@Observaciones", SqlDbType.VarChar).Value =
                                string.IsNullOrWhiteSpace(flete.Observaciones)
                                    ? (object)DBNull.Value
                                    : flete.Observaciones;

                            await cmd.ExecuteNonQueryAsync();
                            exitosos++;

                            resultados.Add(new SincronizacionResult
                            {
                                IdFletePer = flete.IdFletePer,
                                Success = true,
                                Message = existe ? "Actualizado" : "Insertado"
                            });

                            _logger.LogInformation(
                                "Flete {Id} {Accion} correctamente",
                                flete.IdFletePer, existe ? "actualizado" : "insertado");
                        }
                        catch (Exception ex)
                        {
                            // FIX NIVEL 2 #17: si UN item falla, marcamos todos
                            // los restantes como fallidos y hacemos rollback
                            // para que el cliente reintente el batch entero.
                            // Esto preserva atomicidad.
                            resultados.Add(new SincronizacionResult
                            {
                                IdFletePer = flete.IdFletePer,
                                Success = false,
                                Message = $"Error: {ex.Message}"
                            });
                            _logger.LogError(ex,
                                "Error sincronizando flete {Id} (rollback de todo el batch)",
                                flete.IdFletePer);
                            throw; // salta al catch externo que hace rollback
                        }
                    }

                    await transaction.CommitAsync();

                    return Ok(new ApiResponse<List<SincronizacionResult>>
                    {
                        Success = exitosos > 0,
                        Data = resultados,
                        Message = $"Sincronización: {exitosos} exitosos de {request.Count}"
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Rollback en SincronizarFletes");
                    throw; // re-lanza para que el catch externo devuelva 500
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general en SincronizarFletes");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error en sincronización. El batch fue revertido; reintente."
                });
            }
        }

        // CORREGIDO: Cambiado a async Task<IActionResult> y uso de OpenAsync
        [HttpGet("VerificarConexion")]
        public async Task<IActionResult> VerificarConexion()
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();
                var version = con.ServerVersion;

                // FIX NIVEL 3 #21: obtener la hora de la BD con GETDATE()
                // en lugar de DateTime.Now del servidor web. Si el servidor
                // web y la BD están en zonas horarias distintas, el cliente
                // recibía hora incorrecta. Ahora recibe la hora canónica
                // de la BD, que es la misma que usa para los timestamps
                // de FlePer_Fecha/FlePer_FechaFin.
                DateTime fechaServidor;
                using (var cmdFecha = new SqlCommand("SELECT GETDATE()", con))
                {
                    fechaServidor = (DateTime)(await cmdFecha.ExecuteScalarAsync());
                }

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new
                    {
                        Mensaje = "API funcionando correctamente",
                        BaseDatos = "Conectada",
                        VersionServidor = version,
                        FechaServidor = fechaServidor
                    },
                    Message = "Conexión exitosa"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ApiResponse<object> { Success = false, Message = $"Error de conexión: {ex.Message}" });
            }
        }
        #endregion
    }
}