using Microsoft.AspNetCore.Mvc;
using System.Data;
// CORREGIDO: Se eliminó el using System.Data.SqlClient (obsoleto) para evitar conflictos
using Microsoft.Data.SqlClient;
using BusCheckInV2API.Models;

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

        // CORREGIDO: Se eliminó el try-catch innecesario que solo relanzaba la excepción
        private async Task<int> EjecutarConsultaAsync(SqlCommand cmd)
        {
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

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
        [HttpPost("InsertarDetFlete")]
        public async Task<IActionResult> InsertarTb_FlePer_DetFlete([FromBody] DetFleteRequest request)
        {
            if (request == null) return BadRequest(new ApiResponse<object> { Success = false, Message = "Petición inválida" });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                const string query = @"
                    INSERT INTO TB_FlePer_DetFlete (
                        IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre, FlePer_Depto
                    )
                    SELECT 
                        @IdFletePer, @FlePer_CveNomina, @FlePer_Latitud, @FlePer_Longitud, @FlePer_Fecha,
                        CONCAT(LTRIM(RTRIM(e.emp_nombre)),' ',LTRIM(RTRIM(e.emp_paterno)),' ',LTRIM(RTRIM(e.emp_materno))),
                        LTRIM(RTRIM(d.dep_nombre))
                    FROM tb_cat_empleados e
                    LEFT JOIN tb_cat_departamentos d ON d.dep_folio = e.emp_depto
                    WHERE e.emp_clave = @FlePer_CveNomina;";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;
                cmd.Parameters.Add("@FlePer_CveNomina", SqlDbType.Int).Value = request.FlePer_CveNomina;
                cmd.Parameters.Add("@FlePer_Latitud", SqlDbType.Decimal).Value = request.FlePer_Latitud;
                cmd.Parameters.Add("@FlePer_Longitud", SqlDbType.Decimal).Value = request.FlePer_Longitud;
                cmd.Parameters.Add("@FlePer_Fecha", SqlDbType.DateTime).Value = request.FlePer_Fecha;

                var rowsAffected = await EjecutarNonQueryAsync(cmd);

                // CORREGIDO: Retornar ApiResponse<string>
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

        // POST: api/WSBusCheckInV2/InsertarInicioDetFlete
        [HttpPost("InsertarInicioDetFlete")]
        public async Task<IActionResult> InsertarInicioTb_FlePer_DetFlete([FromBody] DetFleteRequest request)
        {
            if (request == null) return BadRequest(new ApiResponse<object> { Success = false, Message = "Petición inválida" });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                // CORREGIDO: Se hardcodea el 0 y 'INICIO' porque la lógica de negocio dicta que el inicio SIEMPRE es 0
                const string query = @"
                    IF NOT EXISTS (SELECT 1 FROM TB_FlePer_DetFlete WHERE IdFletePer = @IdFletePer AND FlePer_CveNomina = 0)
                    BEGIN
                        INSERT INTO TB_FlePer_DetFlete (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                        VALUES (@IdFletePer, 0, @FlePer_Latitud, @FlePer_Longitud, @FlePer_Fecha, 'INICIO');
                    END";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;
                cmd.Parameters.Add("@FlePer_Latitud", SqlDbType.Decimal).Value = request.FlePer_Latitud;
                cmd.Parameters.Add("@FlePer_Longitud", SqlDbType.Decimal).Value = request.FlePer_Longitud;
                cmd.Parameters.Add("@FlePer_Fecha", SqlDbType.DateTime).Value = request.FlePer_Fecha;

                var rowsAffected = await EjecutarNonQueryAsync(cmd);

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
        [HttpPost("InsertarFinDetFlete")]
        public async Task<IActionResult> InsertarFinTb_FlePer_DetFlete([FromBody] DetFleteRequest request)
        {
            if (request == null) return BadRequest(new ApiResponse<object> { Success = false, Message = "Petición inválida" });

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                // CORREGIDO: Se hardcodea el 9999 y 'FIN' porque la lógica de negocio dicta que el fin SIEMPRE es 9999
                const string query = @"
                    IF NOT EXISTS (SELECT 1 FROM TB_FlePer_DetFlete WHERE IdFletePer = @IdFletePer AND FlePer_CveNomina = 9999)
                    BEGIN
                        INSERT INTO TB_FlePer_DetFlete (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                        VALUES (@IdFletePer, 9999, @FlePer_Latitud, @FlePer_Longitud, @FlePer_Fecha, 'FIN');
                    END";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;
                cmd.Parameters.Add("@FlePer_Latitud", SqlDbType.Decimal).Value = request.FlePer_Latitud;
                cmd.Parameters.Add("@FlePer_Longitud", SqlDbType.Decimal).Value = request.FlePer_Longitud;
                cmd.Parameters.Add("@FlePer_Fecha", SqlDbType.DateTime).Value = request.FlePer_Fecha;

                var rowsAffected = await EjecutarNonQueryAsync(cmd);

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
                    // Query que busca el nombre y depto del empleado en la misma inserción
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

                    foreach (var item in request.Items)
                    {
                        using var cmd = new SqlCommand(query, con,
                            (SqlTransaction)transaction);

                        cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;
                        cmd.Parameters.Add("@FlePer_CveNomina", SqlDbType.Int).Value = item.FlePer_CveNomina;
                        cmd.Parameters.Add("@FlePer_Latitud", SqlDbType.Decimal).Value = (decimal)item.FlePer_Latitud;
                        cmd.Parameters.Add("@FlePer_Longitud", SqlDbType.Decimal).Value = (decimal)item.FlePer_Longitud;
                        cmd.Parameters.Add("@FlePer_Fecha", SqlDbType.DateTime).Value = item.FlePer_Fecha;

                        int rows = await cmd.ExecuteNonQueryAsync();

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
            return Ok("Hola a todos");
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

                const string query = @"
                    WITH FletesConEstado AS (
                       SELECT
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
                cmd.Parameters.Add("@Chofer", SqlDbType.VarChar).Value = $"%{chofer}%";
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
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                string query = @"
                    SELECT 
                        fp.IdFletePer, fp.FlePer_Fecha, fp.FlePer_Hora, fp.Prov_Clave, p.prov_nombre AS NombreProveedor,
                        fp.IdDestFlete, r.NomDestFlete AS NombreRuta, fp.FlePer_TipoFlete, fp.FlePer_TipoViaje, 
                        fp.FlePer_Cantidad, fp.FlePer_Status, fp.FlePer_Chofer,
                        (SELECT COUNT(*) FROM Tb_FlePer_DetFlete dfx WHERE dfx.IdFletePer = fp.IdFletePer) AS PuntosRegistrados,
                        CASE WHEN EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete WHERE IdFletePer = fp.IdFletePer AND FlePer_CveNomina = 0) THEN 1 ELSE 0 END AS TieneInicio,
                        CASE WHEN EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete WHERE IdFletePer = fp.IdFletePer AND FlePer_CveNomina = 9999) THEN 1 ELSE 0 END AS TieneFin
                    FROM Tb_FlePer_FletePersonal fp
                    LEFT JOIN Tb_Cat_Proveedor p ON fp.Prov_Clave = p.prov_clave
                    LEFT JOIN Tb_FlePer_Ruta r ON fp.IdDestFlete = r.IdDestFlete
                    WHERE fp.FlePer_Chofer LIKE @Chofer
                      AND fp.FlePer_Fecha >= DATEADD(DAY, -@Dias, CAST(GETDATE() AS DATE)) ";

                if (soloPendientes)
                {
                    query += @"
                        AND EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete df WHERE df.IdFletePer = fp.IdFletePer AND df.FlePer_CveNomina = 0)
                        AND NOT EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete df WHERE df.IdFletePer = fp.IdFletePer AND df.FlePer_CveNomina = 9999) ";
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
                        TieneFin = reader["TieneFin"] != DBNull.Value ? Convert.ToInt32(reader["TieneFin"]) : 0
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

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();
                using var transaction = con.BeginTransaction();

                try
                {
                    const string updateFlete = @"
                        UPDATE Tb_FlePer_FletePersonal 
                        SET FlePer_Cantidad = @CantidadReal, FlePer_FechaFin = GETDATE(), FlePer_Observaciones = @Observaciones
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

                    const string insertDetalle = @"
                        INSERT INTO TB_FlePer_DetFlete (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                        VALUES (@IdFletePer, 9999, @Latitud, @Longitud, GETDATE(), 'FIN')";

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

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                // CORREGIDO: Se agregó transacción para mantener integridad entre el UPDATE y el INSERT
                using var transaction = con.BeginTransaction();

                try
                {
                    const string query = @"
                        UPDATE Tb_FlePer_FletePersonal 
                        SET FlePer_Status = 'Iniciado', FlePer_Fecha = ISNULL(FlePer_FechaInicio, GETDATE())
                        WHERE IdFletePer = @IdFletePer";

                    using var cmd = new SqlCommand(query, con, transaction);
                    cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = request.IdFletePer;

                    var rowsAffected = await cmd.ExecuteNonQueryAsync();

                    if (rowsAffected == 0)
                    {
                        transaction.Rollback();
                        return NotFound(new ApiResponse<object> { Success = false, Message = "Flete no encontrado" });
                    }

                    const string insertDetalle = @"
                        INSERT INTO TB_FlePer_DetFlete (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                        VALUES (@IdFletePer, 0, @Latitud, @Longitud, GETDATE(), 'Reanudado')";

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

            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                var resultados = new List<SincronizacionResult>();
                int exitosos = 0;

                foreach (var flete in request)
                {
                    try
                    {
                        // ── Parseo defensivo de tipos temporales ─────────────────
                        DateTime fecha = DateTime.TryParse(flete.Fecha, out var f)
                            ? f : DateTime.Now;
                        TimeSpan hora = TimeSpan.TryParse(flete.Hora, out var h)
                            ? h : DateTime.Now.TimeOfDay;

                        // ── Verificar si ya existe el registro ───────────────────
                        const string checkQuery =
                            "SELECT COUNT(1) FROM Tb_FlePer_FletePersonal " +
                            "WHERE IdFletePer = @IdFletePer";

                        using var checkCmd = new SqlCommand(checkQuery, con);
                        checkCmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = flete.IdFletePer;
                        bool existe = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                        // ── Queries con @Fecha y @Hora INCLUIDOS ─────────────────
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

                        using var cmd = new SqlCommand(existe ? updateQuery : insertQuery, con);

                        // ── CORRECCIÓN CRÍTICA: TODOS los parámetros agregados ───
                        cmd.Parameters.Add("@IdFletePer", SqlDbType.Int).Value = flete.IdFletePer;
                        cmd.Parameters.Add("@Fecha", SqlDbType.DateTime).Value = fecha;   // ← antes faltaba
                        cmd.Parameters.Add("@Hora", SqlDbType.Time).Value = hora;    // ← antes faltaba
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
                        resultados.Add(new SincronizacionResult
                        {
                            IdFletePer = flete.IdFletePer,
                            Success = false,
                            Message = $"Error: {ex.Message}"
                        });
                        _logger.LogError(ex,
                            "Error sincronizando flete {Id}", flete.IdFletePer);
                    }
                }

                return Ok(new ApiResponse<List<SincronizacionResult>>
                {
                    Success = exitosos > 0,
                    Data = resultados,
                    Message = $"Sincronización: {exitosos} exitosos de {request.Count}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general en SincronizarFletes");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error en sincronización"
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

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new { Mensaje = "API funcionando correctamente", BaseDatos = "Conectada", VersionServidor = version, FechaServidor = DateTime.Now },
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