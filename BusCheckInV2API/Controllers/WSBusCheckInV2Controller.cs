using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Text.Json.Serialization;
using BusCheckInV2API.Models;

namespace BusCheckInV2API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]  // Ruta base: /api/WSBusCheckInV2
    public class WSBusCheckInV2Controller : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<WSBusCheckInV2Controller> _logger;
        public WSBusCheckInV2Controller(IConfiguration configuration, ILogger<WSBusCheckInV2Controller> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        // Método privado para ejecutar comandos async
        private async Task<int> EjecutarConsultaAsync(SqlCommand cmd)
        {
            try
            {
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar consulta");
                throw;
            }
        }

        private async Task<int> EjecutarNonQueryAsync(SqlCommand cmd)
        {
            try
            {
                return await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar non-query");
                throw;
            }
        }

        #region ENDPOINTS ESCANEO DE CODIGOS
        // POST: api/WSBusCheckInV2/InsertarFletePersonal
        [HttpPost("InsertarFletePersonal")]
        public async Task<IActionResult> InsertarTb_FlePer_FletePersonal([FromBody] FletePersonalRequest request)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                const string consulta = @"
                    INSERT INTO Tb_FlePer_FletePersonal (FlePer_Fecha, FlePer_Hora, Prov_Clave, IdDestFlete, FlePer_TipoFlete, FlePer_TipoViaje, FlePer_Cantidad, FlePer_Status, FlePer_Chofer)
                    VALUES (TRY_CAST(@FlePer_Fecha as DATETIME), TRY_CAST(@FlePer_Hora as TIME), @Prov_Clave, @IdDestFlete, @FlePer_TipoFlete, @FlePer_TipoViaje, @FlePer_Cantidad, @FlePer_Status, @FlePer_Chofer);
                    SELECT SCOPE_IDENTITY();";

                using var cmd = new SqlCommand(consulta, con);
                cmd.Parameters.AddWithValue("@FlePer_Fecha", request.Fecha);
                cmd.Parameters.AddWithValue("@FlePer_Hora", request.Hora);
                cmd.Parameters.AddWithValue("@Prov_Clave", request.ClaveProveedor);
                cmd.Parameters.AddWithValue("@IdDestFlete", request.IdDestFlete);
                cmd.Parameters.AddWithValue("@FlePer_TipoFlete", request.TipoFlete);
                cmd.Parameters.AddWithValue("@FlePer_TipoViaje", request.TipoViaje);
                cmd.Parameters.AddWithValue("@FlePer_Cantidad", request.Cantidad);
                cmd.Parameters.AddWithValue("@FlePer_Status", request.Estatus);
                cmd.Parameters.AddWithValue("@FlePer_Chofer", request.Chofer);

                var resultado = await EjecutarConsultaAsync(cmd);
                return Ok(resultado.ToString());
            }
            catch (Exception ex)
            {
                return BadRequest("Error al insertar registro: " + ex.Message);
            }
        }

        // POST: api/WSBusCheckInV2/InsertarDetFlete
        [HttpPost("InsertarDetFlete")]
        public async Task<IActionResult> InsertarTb_FlePer_DetFlete([FromBody] DetFleteRequest request)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                const string query = @"
            INSERT INTO TB_FlePer_DetFlete (
                IdFletePer, 
                FlePer_CveNomina, 
                FlePer_Latitud, 
                FlePer_Longitud, 
                FlePer_Fecha, 
                FlePer_Nombre, 
                FlePer_Depto
            )
            SELECT 
                @IdFletePer,
                @FlePer_CveNomina,
                @FlePer_Latitud,
                @FlePer_Longitud,
                @FlePer_Fecha,
                CONCAT(LTRIM(RTRIM(e.emp_nombre)),' ',LTRIM(RTRIM(e.emp_paterno)),' ',LTRIM(RTRIM(e.emp_materno))),
                LTRIM(RTRIM(d.dep_nombre))
            FROM tb_cat_empleados e
            LEFT JOIN tb_cat_departamentos d ON d.dep_folio = e.emp_depto
            WHERE e.emp_clave = @FlePer_CveNomina;";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);
                cmd.Parameters.AddWithValue("@FlePer_CveNomina", request.FlePer_CveNomina);
                cmd.Parameters.AddWithValue("@FlePer_Latitud", request.FlePer_Latitud);
                cmd.Parameters.AddWithValue("@FlePer_Longitud", request.FlePer_Longitud);
                cmd.Parameters.AddWithValue("@FlePer_Fecha", request.FlePer_Fecha);

                var rowsAffected = await EjecutarNonQueryAsync(cmd);
                return Ok(rowsAffected > 0 ? "Insertado correctamente" : "No se pudo insertar");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error: {ex.Message}");
            }
        }

        // POST: api/WSBusCheckInV2/InsertarInicioDetFlete
        [HttpPost("InsertarInicioDetFlete")]
        public async Task<IActionResult> InsertarInicioTb_FlePer_DetFlete([FromBody] DetFleteRequest request)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                const string query = @"
                    IF NOT EXISTS (SELECT 1 FROM TB_FlePer_DetFlete WHERE IdFletePer = @IdFletePer AND FlePer_CveNomina = 0)
                    BEGIN
                        INSERT INTO TB_FlePer_DetFlete (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                        VALUES (@IdFletePer, @FlePer_CveNomina, @FlePer_Latitud, @FlePer_Longitud, @FlePer_Fecha, @FlePer_Nombre);
                    END";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);
                cmd.Parameters.AddWithValue("@FlePer_CveNomina", request.FlePer_CveNomina);
                cmd.Parameters.AddWithValue("@FlePer_Latitud", request.FlePer_Latitud);
                cmd.Parameters.AddWithValue("@FlePer_Longitud", request.FlePer_Longitud);
                cmd.Parameters.AddWithValue("@FlePer_Fecha", request.FlePer_Fecha);
                cmd.Parameters.AddWithValue("@FlePer_Nombre", request.FlePer_Nombre);

                var rowsAffected = await EjecutarNonQueryAsync(cmd);
                return Ok(rowsAffected > 0 ? "Insertado correctamente" : "No se pudo insertar");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error: {ex.Message}");
            }
        }

        // PUT: api/WSBusCheckInV2/UpdateFletePersonal
        [HttpPut("UpdateFletePersonal")]
        public async Task<IActionResult> UpdateTb_FlePer_FletePersonal([FromBody] UpdateFleteRequest request)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                const string query = @"
                    UPDATE Tb_FlePer_FletePersonal
                    SET FlePer_Cantidad = @FlePer_Cantidad, FlePer_Status = 'A'
                    WHERE IdFletePer = @IdFletePer;";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@FlePer_Cantidad", request.FlePer_Cantidad);
                cmd.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);

                var rowsAffected = await EjecutarNonQueryAsync(cmd);
                return Ok(rowsAffected > 0 ? "Actualizado correctamente" : "No se pudo actualizar");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error: {ex.Message}");
            }
        }

        // POST: api/WSBusCheckInV2/InsertarFinDetFlete
        [HttpPost("InsertarFinDetFlete")]
        public async Task<IActionResult> InsertarFinTb_FlePer_DetFlete([FromBody] DetFleteRequest request)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                const string query = @"
                    IF NOT EXISTS (SELECT 1 FROM TB_FlePer_DetFlete WHERE IdFletePer = @IdFletePer AND FlePer_CveNomina = 9999)
                    BEGIN
                        INSERT INTO TB_FlePer_DetFlete (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                        VALUES (@IdFletePer, @FlePer_CveNomina, @FlePer_Latitud, @FlePer_Longitud, @FlePer_Fecha, @FlePer_Nombre);
                    END";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);
                cmd.Parameters.AddWithValue("@FlePer_CveNomina", request.FlePer_CveNomina);
                cmd.Parameters.AddWithValue("@FlePer_Latitud", request.FlePer_Latitud);
                cmd.Parameters.AddWithValue("@FlePer_Longitud", request.FlePer_Longitud);
                cmd.Parameters.AddWithValue("@FlePer_Fecha", request.FlePer_Fecha);
                cmd.Parameters.AddWithValue("@FlePer_Nombre", request.FlePer_Nombre);

                var rowsAffected = await EjecutarNonQueryAsync(cmd);
                return Ok(rowsAffected > 0 ? "Insertado correctamente" : "No se pudo insertar");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error: {ex.Message}");
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
                                WHEN
                                    EXISTS (
                                        SELECT 1
                                        FROM Tb_FlePer_DetFlete df
                                        WHERE df.IdFletePer = fp.IdFletePer
                                          AND df.FlePer_CveNomina = 0
                                    )
                                    AND NOT EXISTS (
                                        SELECT 1
                                        FROM Tb_FlePer_DetFlete df
                                        WHERE df.IdFletePer = fp.IdFletePer
                                          AND df.FlePer_CveNomina = 9999
                                          AND df.FlePer_Nombre = 'FIN'
                                    )
                                THEN 1
                                ELSE 0
                            END AS EsPendiente
                        FROM Tb_FlePer_FletePersonal fp
                        WHERE fp.FlePer_Chofer IS NOT NULL
                          AND LTRIM(RTRIM(fp.FlePer_Chofer)) <> '' AND fp.FlePer_Fecha >= DATEADD(DAY, -7, CAST(GETDATE() AS DATE))
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
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor"
                });
            }
        }

        // Agrega este endpoint temporal para pruebas:
        [HttpGet("ObtenerFletesPorChoferTodos")]
        public async Task<IActionResult> ObtenerFletesPorChoferTodos(string chofer, int dias = 3)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                string query = @"
            SELECT 
                fp.IdFletePer,
                fp.FlePer_Fecha,
                fp.FlePer_Hora,
                fp.Prov_Clave,
                p.prov_nombre AS NombreProveedor,
                fp.IdDestFlete,
                r.NomDestFlete AS NombreRuta,
                fp.FlePer_TipoFlete,
                fp.FlePer_TipoViaje,
                fp.FlePer_Cantidad,
                fp.FlePer_Status,
                fp.FlePer_Chofer,
                (
                    SELECT COUNT(*)
                    FROM Tb_FlePer_DetFlete dfx
                    WHERE dfx.IdFletePer = fp.IdFletePer
                ) AS PuntosRegistrados
            FROM Tb_FlePer_FletePersonal fp
            LEFT JOIN Tb_Cat_Proveedor p 
                   ON fp.Prov_Clave = p.prov_clave
            LEFT JOIN Tb_FlePer_Ruta r 
                   ON fp.IdDestFlete = r.IdDestFlete
            WHERE fp.FlePer_Chofer LIKE @Chofer
              AND fp.FlePer_Fecha >= DATEADD(DAY, -@Dias, CAST(GETDATE() AS DATE))
            ORDER BY fp.FlePer_Fecha DESC,
                     fp.FlePer_Hora DESC";

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
                        Fecha = reader["FlePer_Fecha"].ToString(),
                        Hora = reader["FlePer_Hora"].ToString(),
                        ProveedorClave = reader["Prov_Clave"].ToString(),
                        ProveedorNombre = reader["NombreProveedor"].ToString(),
                        IdDestFlete = reader["IdDestFlete"] != DBNull.Value ? Convert.ToInt32(reader["IdDestFlete"]) : 0,
                        RutaNombre = reader["NombreRuta"].ToString(),
                        TipoFlete = reader["FlePer_TipoFlete"].ToString(),
                        TipoViaje = reader["FlePer_TipoViaje"].ToString(),
                        Cantidad = reader["FlePer_Cantidad"] != DBNull.Value ? Convert.ToInt32(reader["FlePer_Cantidad"]) : 0,
                        Estatus = reader["FlePer_Status"].ToString(),
                        Chofer = reader["FlePer_Chofer"].ToString(),
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
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor"
                });
            }
        }

        // GET: api/WSBusCheckInV2/ObtenerFletesPorChofer
        [HttpGet("ObtenerFletesPorChofer")]
        public async Task<IActionResult> ObtenerFletesPorChofer(string chofer, int dias = 3, bool soloPendientes = false)  // Agrega este parámetro
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                string query = @"
            SELECT 
                fp.IdFletePer,
                fp.FlePer_Fecha,
                fp.FlePer_Hora,
                fp.Prov_Clave,
                p.prov_nombre AS NombreProveedor,
                fp.IdDestFlete,
                r.NomDestFlete AS NombreRuta,
                fp.FlePer_TipoFlete,
                fp.FlePer_TipoViaje,
                fp.FlePer_Cantidad,
                fp.FlePer_Status,
                fp.FlePer_Chofer,
                (
                    SELECT COUNT(*)
                    FROM Tb_FlePer_DetFlete dfx
                    WHERE dfx.IdFletePer = fp.IdFletePer
                ) AS PuntosRegistrados,
                -- Agregar estas columnas para determinar si está pendiente
                CASE 
                    WHEN EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete WHERE IdFletePer = fp.IdFletePer AND FlePer_CveNomina = 0) THEN 1
                    ELSE 0
                END AS TieneInicio,
                CASE 
                    WHEN EXISTS (SELECT 1 FROM Tb_FlePer_DetFlete WHERE IdFletePer = fp.IdFletePer AND FlePer_CveNomina = 9999) THEN 1
                    ELSE 0
                END AS TieneFin
            FROM Tb_FlePer_FletePersonal fp
            LEFT JOIN Tb_Cat_Proveedor p 
                   ON fp.Prov_Clave = p.prov_clave
            LEFT JOIN Tb_FlePer_Ruta r 
                   ON fp.IdDestFlete = r.IdDestFlete
            WHERE fp.FlePer_Chofer LIKE @Chofer
              AND fp.FlePer_Fecha >= DATEADD(DAY, -@Dias, CAST(GETDATE() AS DATE))
            ";

                // Solo aplicar filtro de pendientes si se solicita
                if (soloPendientes)
                {
                    query += @"
                AND EXISTS (
                    SELECT 1
                    FROM Tb_FlePer_DetFlete df
                    WHERE df.IdFletePer = fp.IdFletePer
                      AND df.FlePer_CveNomina = 0
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM Tb_FlePer_DetFlete df
                    WHERE df.IdFletePer = fp.IdFletePer
                      AND df.FlePer_CveNomina = 9999
                )";
                }

                query += @"
            ORDER BY fp.FlePer_Fecha DESC,
                     fp.FlePer_Hora DESC";

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
                        Fecha = reader["FlePer_Fecha"].ToString(),
                        Hora = reader["FlePer_Hora"].ToString(),
                        ProveedorClave = reader["Prov_Clave"].ToString(),
                        ProveedorNombre = reader["NombreProveedor"].ToString(),
                        IdDestFlete = reader["IdDestFlete"] != DBNull.Value ? Convert.ToInt32(reader["IdDestFlete"]) : 0,
                        RutaNombre = reader["NombreRuta"].ToString(),
                        TipoFlete = reader["FlePer_TipoFlete"].ToString(),
                        TipoViaje = reader["FlePer_TipoViaje"].ToString(),
                        Cantidad = reader["FlePer_Cantidad"] != DBNull.Value ? Convert.ToInt32(reader["FlePer_Cantidad"]) : 0,
                        Estatus = reader["FlePer_Status"].ToString(),
                        Chofer = reader["FlePer_Chofer"].ToString(),
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
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor"
                });
            }
        }

        // POST: api/WSBusCheckInV2/ValidarYFinalizarFlete
        [HttpPost("ValidarYFinalizarFlete")]
        public async Task<IActionResult> ValidarYFinalizarFlete([FromBody] FinalizarFleteRequest request)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                // Iniciar transacción
                using var transaction = con.BeginTransaction();

                try
                {
                    // 1. Actualizar el flete
                    const string updateFlete = @"
                        UPDATE Tb_FlePer_FletePersonal 
                        SET FlePer_Cantidad = @CantidadReal,
                            FlePer_FechaFin = GETDATE(),
                            FlePer_Observaciones = @Observaciones
                        WHERE IdFletePer = @IdFletePer";

                    using var cmdUpdate = new SqlCommand(updateFlete, con, transaction);
                    cmdUpdate.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);
                    cmdUpdate.Parameters.AddWithValue("@CantidadReal", request.CantidadReal);
                    cmdUpdate.Parameters.AddWithValue("@Observaciones", request.Observaciones ?? string.Empty);

                    var rowsAffected = await cmdUpdate.ExecuteNonQueryAsync();

                    if (rowsAffected == 0)
                    {
                        transaction.Rollback();
                        return NotFound(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "Flete no encontrado"
                        });
                    }

                    // 2. Insertar punto final de flete
                    const string insertDetalle = @"
                        INSERT INTO TB_FlePer_DetFlete 
                            (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                        VALUES 
                            (@IdFletePer, 9999, @Latitud, @Longitud, GETDATE(), 'FIN')";

                    using var cmdDetalle = new SqlCommand(insertDetalle, con, transaction);
                    cmdDetalle.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);
                    cmdDetalle.Parameters.AddWithValue("@Latitud", request.Latitud);
                    cmdDetalle.Parameters.AddWithValue("@Longitud", request.Longitud);

                    await cmdDetalle.ExecuteNonQueryAsync();

                    #region ACONDICIONAMIENTO PARA AUDITORIA
                    // 3. Registrar en tabla de auditoría (opcional)
                    /*const string insertAuditoria = @"
                        INSERT INTO Tb_FlePer_Auditoria 
                            (IdFletePer, Accion, Usuario, Fecha, Detalles)
                        VALUES 
                            (@IdFletePer, 'Finalizado', @Usuario, GETDATE(), @Detalles)";

                    using var cmdAuditoria = new SqlCommand(insertAuditoria, con, transaction);
                    cmdAuditoria.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);
                    cmdAuditoria.Parameters.AddWithValue("@Usuario", request.Usuario ?? "Sistema");
                    cmdAuditoria.Parameters.AddWithValue("@Detalles",
                        $"Flete finalizado. Pasajeros: {request.CantidadReal}. Obs: {request.Observaciones}");

                    await cmdAuditoria.ExecuteNonQueryAsync();*/
                    #endregion

                    // Confirmar transacción
                    transaction.Commit();

                    return Ok(new ApiResponse<object>
                    {
                        Success = true,
                        Message = "Flete finalizado exitosamente"
                    });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ValidarYFinalizarFlete");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error al finalizar flete"
                });
            }
        }

        // POST: api/WSBusCheckInV2/CancelarFlete
        [HttpPost("CancelarFlete")]
        public async Task<IActionResult> CancelarFlete([FromBody] CancelarFleteRequest request)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                const string query = @"
                    UPDATE Tb_FlePer_FletePersonal 
                    SET FlePer_Status = 'C',
                        FlePer_Observaciones = @Observaciones,
                        FlePer_FechaFin = GETDATE()
                    WHERE IdFletePer = @IdFletePer";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);
                cmd.Parameters.AddWithValue("@Observaciones",
                    $"CANCELADO - Motivo: {request.Motivo}. Detalles: {request.Detalles}");

                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Flete no encontrado"
                    });
                }

                #region ACONDICIONAMIENTO PARA AUDITORIA
                // Registrar auditoría
                /*const string auditoria = @"
                    INSERT INTO Tb_FlePer_Auditoria 
                        (IdFletePer, Accion, Usuario, Fecha, Detalles)
                    VALUES 
                        (@IdFletePer, 'Cancelado', @Usuario, GETDATE(), @Detalles)";

                using var cmdAudit = new SqlCommand(auditoria, con);
                cmdAudit.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);
                cmdAudit.Parameters.AddWithValue("@Usuario", request.Usuario ?? "Sistema");
                cmdAudit.Parameters.AddWithValue("@Detalles",
                    $"Flete cancelado. Motivo: {request.Motivo}. Detalles: {request.Detalles}");

                await cmdAudit.ExecuteNonQueryAsync();*/
                #endregion

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "Flete cancelado exitosamente"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en CancelarFlete");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error al cancelar flete"
                });
            }
        }

        // POST: api/WSBusCheckInV2/ReanudarFlete
        [HttpPost("ReanudarFlete")]
        public async Task<IActionResult> ReanudarFlete([FromBody] ReanudarFleteRequest request)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                await con.OpenAsync();

                const string query = @"
                    UPDATE Tb_FlePer_FletePersonal 
                    SET FlePer_Status = 'Iniciado',
                        FlePer_Fecha = ISNULL(FlePer_FechaInicio, GETDATE())
                    WHERE IdFletePer = @IdFletePer";

                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Flete no encontrado"
                    });
                }

                // Insertar punto de reanudación
                const string insertDetalle = @"
                    INSERT INTO TB_FlePer_DetFlete 
                        (IdFletePer, FlePer_CveNomina, FlePer_Latitud, FlePer_Longitud, FlePer_Fecha, FlePer_Nombre)
                    VALUES 
                        (@IdFletePer, 0, @Latitud, @Longitud, GETDATE(), 'Reanudado')";

                using var cmdDetalle = new SqlCommand(insertDetalle, con);
                cmdDetalle.Parameters.AddWithValue("@IdFletePer", request.IdFletePer);
                cmdDetalle.Parameters.AddWithValue("@Latitud", request.Latitud);
                cmdDetalle.Parameters.AddWithValue("@Longitud", request.Longitud);

                await cmdDetalle.ExecuteNonQueryAsync();

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "Flete reanudado exitosamente"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ReanudarFlete");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error al reanudar flete"
                });
            }
        }

        // POST: api/WSBusCheckInV2/SincronizarFletes
        [HttpPost("SincronizarFletes")]
        public async Task<IActionResult> SincronizarFletes([FromBody] List<SincronizacionRequest> request)
        {
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
                        // Verificar si el flete ya existe
                        const string checkQuery = "SELECT COUNT(*) FROM Tb_FlePer_FletePersonal WHERE IdFletePer = @IdFletePer";
                        using var checkCmd = new SqlCommand(checkQuery, con);
                        checkCmd.Parameters.AddWithValue("@IdFletePer", flete.IdFletePer);

                        var existe = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                        string query;
                        if (existe)
                        {
                            // Actualizar
                            query = @"
                                UPDATE Tb_FlePer_FletePersonal 
                                SET FlePer_Fecha = @Fecha,
                                    FlePer_Hora = @Hora,
                                    Prov_Clave = @ProvClave,
                                    IdDestFlete = @IdDestFlete,
                                    FlePer_TipoFlete = @TipoFlete,
                                    FlePer_TipoViaje = @TipoViaje,
                                    FlePer_Cantidad = @Cantidad,
                                    FlePer_Status = @Status,
                                    FlePer_Chofer = @Chofer,
                                    FlePer_Observaciones = @Observaciones
                                WHERE IdFletePer = @IdFletePer";
                        }
                        else
                        {
                            // Insertar nuevo
                            query = @"
                                INSERT INTO Tb_FlePer_FletePersonal 
                                    (IdFletePer, FlePer_Fecha, FlePer_Hora, Prov_Clave, IdDestFlete, 
                                     FlePer_TipoFlete, FlePer_TipoViaje, FlePer_Cantidad, FlePer_Status, 
                                     FlePer_Chofer, FlePer_Observaciones)
                                VALUES 
                                    (@IdFletePer, @Fecha, @Hora, @ProvClave, @IdDestFlete, 
                                     @TipoFlete, @TipoViaje, @Cantidad, @Status, 
                                     @Chofer, @Observaciones)";
                        }

                        using var cmd = new SqlCommand(query, con);
                        cmd.Parameters.AddWithValue("@IdFletePer", flete.IdFletePer);
                        cmd.Parameters.AddWithValue("@Fecha", flete.Fecha);
                        cmd.Parameters.AddWithValue("@Hora", flete.Hora);
                        cmd.Parameters.AddWithValue("@ProvClave", flete.ProvClave);
                        cmd.Parameters.AddWithValue("@IdDestFlete", flete.IdDestFlete);
                        cmd.Parameters.AddWithValue("@TipoFlete", flete.TipoFlete);
                        cmd.Parameters.AddWithValue("@TipoViaje", flete.TipoViaje);
                        cmd.Parameters.AddWithValue("@Cantidad", flete.Cantidad);
                        cmd.Parameters.AddWithValue("@Status", flete.Status);
                        cmd.Parameters.AddWithValue("@Chofer", flete.Chofer);
                        cmd.Parameters.AddWithValue("@Observaciones", flete.Observaciones ?? string.Empty);

                        await cmd.ExecuteNonQueryAsync();
                        exitosos++;

                        resultados.Add(new SincronizacionResult
                        {
                            IdFletePer = flete.IdFletePer,
                            Success = true,
                            Message = existe ? "Actualizado" : "Insertado"
                        });
                    }
                    catch (Exception ex)
                    {
                        resultados.Add(new SincronizacionResult
                        {
                            IdFletePer = flete.IdFletePer,
                            Success = false,
                            Message = $"Error: {ex.Message}"
                        });
                        _logger.LogError(ex, $"Error sincronizando flete {flete.IdFletePer}");
                    }
                }

                return Ok(new ApiResponse<List<SincronizacionResult>>
                {
                    Success = true,
                    Data = resultados,
                    Message = $"Sincronización completada: {exitosos} exitosos de {request.Count}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en SincronizarFletes");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error en sincronización"
                });
            }
        }

        // GET: api/WSBusCheckInV2/VerificarConexion
        [HttpGet("VerificarConexion")]
        public IActionResult VerificarConexion()
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
                var version = con.ServerVersion;
                con.Close();

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new
                    {
                        Mensaje = "API funcionando correctamente",
                        BaseDatos = "Conectada",
                        VersionServidor = version,
                        FechaServidor = DateTime.Now
                    },
                    Message = "Conexión exitosa"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error de conexión: {ex.Message}"
                });
            }
        }
        #endregion

    }
}
