using DbBridge;
using Microsoft.Data.SqlClient;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// Default connection string for environments where env vars are not available
// MODIFY THESE VALUES according to your hosting environment:
// - Server: your SQL Server hostname/IP
// - Database: your database name
// - User Id: your SQL Server username
// - Password: your SQL Server password
// const string DEFAULT_SQL_CONN = "Server=pilates.centrosportivolife.it,15432;Database=MSSql32801;User Id=sa;Password=AspiProdSecurePass2024!;TrustServerCertificate=True;";

// Production database connection string (temporary for testing)
const string DEFAULT_SQL_CONN = "Data Source=yb6irbxs4f.zoneyb.mssql-aruba.it;Initial Catalog=MSSql32801;User ID=MSSql32801;Password=bb81a3cc;TrustServerCertificate=True;";

var app = builder.Build();

// Middleware: richiedi header segreto per l'autenticazione
app.UseMiddleware<SecretAuthMiddleware>();

// Health
app.MapGet("/health", () => Results.Json(new { success = true }));

// Eventi by tecnico con filtri
app.MapPost("/db/eventi/tecnico", async (EventiByTecnicoReq req) =>
{
    var connStr = Environment.GetEnvironmentVariable("SQL_CONN") ?? DEFAULT_SQL_CONN;

    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Costruisci la query dinamicamente
        var whereConditions = new List<string> { "RDI_OPEN.ESITO_EVENTO = 0" };
        var parameters = new Dictionary<string, object> { ["@tecnicoId"] = req.TecnicoId };

        // Filtro is_planned
        if (!string.IsNullOrEmpty(req.IsPlanned) && req.IsPlanned != "all")
        {
            if (req.IsPlanned == "true")
                whereConditions.Add("(RDI_OPEN.DATA_PIANIFICA IS NOT NULL AND RDI_OPEN.DATA_PIANIFICA != '')");
            else if (req.IsPlanned == "false")
                whereConditions.Add("(RDI_OPEN.DATA_PIANIFICA IS NULL OR RDI_OPEN.DATA_PIANIFICA = '')");
        }

        // Altri filtri
        if (!string.IsNullOrEmpty(req.Search))
        {
            whereConditions.Add(@"(
                RDI_OPEN.WORKORDER LIKE @search OR 
                RDI_OPEN.CODCHI_E LIKE @search OR 
                RDI_OPEN.RAG_SOC_CF LIKE @search OR 
                RDI_OPEN.COMUNE_DESTINAZIONE LIKE @search OR 
                RDI_OPEN.PROVINCIA_DESTINAZIONE LIKE @search OR 
                RDI_OPEN.INDI_DEST_MERCE LIKE @search OR 
                RDI_OPEN.DEVICE LIKE @search OR 
                RDI_OPEN.CONTATTO LIKE @search OR 
                RDI_OPEN.DESCRIZIONE_EVENTO LIKE @search OR 
                RDI_OPEN.NOTE_EVENTO LIKE @search OR 
                RDI_OPEN.DES_TIPO_EVENTO LIKE @search OR 
                RDI_OPEN.UTENTE_INOLTRO LIKE @search OR 
                CONVERT(VARCHAR, RDI_OPEN.DATA_RICHIESTA_CLI, 23) LIKE @search
            )");
            parameters["@search"] = $"%{req.Search}%";
        }

        if (!string.IsNullOrEmpty(req.Cliente))
        {
            whereConditions.Add("RDI_OPEN.RAG_SOC_CF LIKE @cliente");
            parameters["@cliente"] = $"%{req.Cliente}%";
        }

        if (!string.IsNullOrEmpty(req.Comune))
        {
            whereConditions.Add("RDI_OPEN.COMUNE_DESTINAZIONE LIKE @comune");
            parameters["@comune"] = $"%{req.Comune}%";
        }

        if (!string.IsNullOrEmpty(req.Provincia))
        {
            whereConditions.Add("RDI_OPEN.PROVINCIA_DESTINAZIONE = @provincia");
            parameters["@provincia"] = req.Provincia;
        }

        if (!string.IsNullOrEmpty(req.DateFrom))
        {
            whereConditions.Add("RDI_OPEN.DATA_RICHIESTA_CLI >= @dateFrom");
            parameters["@dateFrom"] = req.DateFrom;
        }

        if (!string.IsNullOrEmpty(req.DateTo))
        {
            whereConditions.Add("RDI_OPEN.DATA_RICHIESTA_CLI <= @dateTo");
            parameters["@dateTo"] = req.DateTo;
        }

        if (!string.IsNullOrEmpty(req.Priority))
        {
            whereConditions.Add("RDI_OPEN.PRIORITA = @priority");
            parameters["@priority"] = req.Priority;
        }

        // Filtro per tecnico_id obbligatorio
        whereConditions.Add("RDI_OPEN.TECNICO_ID = @tecnicoId");
        parameters["@tecnicoId"] = req.TecnicoId;

        var whereClause = string.Join(" AND ", whereConditions);

        // Ordinamento
        var orderBy = "ORDER BY RDI_OPEN.PRIORITA DESC, RDI_OPEN.DATA_RICHIESTA_CLI DESC";
        if (!string.IsNullOrEmpty(req.Sort))
        {
            orderBy = req.Sort switch
            {
                "date_desc" => "ORDER BY RDI_OPEN.DATA_RICHIESTA_CLI DESC",
                "date_asc" => "ORDER BY RDI_OPEN.DATA_RICHIESTA_CLI ASC",
                "priority_desc" => "ORDER BY RDI_OPEN.PRIORITA DESC, RDI_OPEN.DATA_RICHIESTA_CLI DESC",
                "priority_asc" => "ORDER BY RDI_OPEN.PRIORITA ASC, RDI_OPEN.DATA_RICHIESTA_CLI DESC",
                "cliente_asc" => "ORDER BY RDI_OPEN.RAG_SOC_CF ASC",
                "cliente_desc" => "ORDER BY RDI_OPEN.RAG_SOC_CF DESC",
                "comune_asc" => "ORDER BY RDI_OPEN.COMUNE_DESTINAZIONE ASC",
                "comune_desc" => "ORDER BY RDI_OPEN.COMUNE_DESTINAZIONE DESC",
                _ => orderBy
            };
        }

        // Query count
        var countSql = $@"
            SELECT COUNT(*) as total_count
            FROM MSSql32801.RDI_OPEN
            WHERE {whereClause}";

        await using var countCmd = new SqlCommand(countSql, conn) { CommandTimeout = 15 };
        foreach (var (k, v) in parameters)
            countCmd.Parameters.AddWithValue(k, v ?? DBNull.Value);

        var total = (int)await countCmd.ExecuteScalarAsync();

        // Query data
        var dataSql = $@"
            SELECT RDI_OPEN.EVENTO_ID as evento_id, RDI_OPEN.WORKORDER as workorder, RDI_OPEN.DESCRIZIONE_EVENTO as descrizione_evento,
                   RDI_OPEN.STATO as stato, RDI_OPEN.ESITO_EVENTO as esito_evento, RDI_OPEN.UTENTE_INOLTRO as utente_inoltro,
                   RDI_OPEN.TECNICO_ID as tecnico_id, RDI_OPEN.DATA_RICHIESTA_CLI as data_richiesta_client,
                   RDI_OPEN.ORA_RICHIESTA_CLI as ora_richiesta_client, RDI_OPEN.DATA_MAX as data_max,
                   RDI_OPEN.ORA_MAX as ora_max, RDI_OPEN.DATA_PIANIFICA as data_pianifica, RDI_OPEN.PRIORITA as priorita,
                   RDI_OPEN.COD_TIPO_EVENTO as cod_tipo_evento, RDI_OPEN.DES_TIPO_EVENTO as des_tipo_evento,
                   RDI_OPEN.NOTE_EVENTO as note_evento, '' as nome_tecnico,
                   RDI_OPEN.DES_STATO as des_stato, RDI_OPEN.RAG_SOC_CF as cliente, RDI_OPEN.COD_CF as cod_cf, RDI_OPEN.CODCHI_E as codchi_e, RDI_OPEN.CONTATTO as contatto,
                   RDI_OPEN.TEL_CF as telefono, RDI_OPEN.TEL_CONTATTO as telefono_contatto,
                   RDI_OPEN.CELL_CONTATTO as cellulare, RDI_OPEN.DEVICE as device,
                   RDI_OPEN.MATRICOLA_DEVICE as matricola_device,
                   RDI_OPEN.INDI_DEST_MERCE as indirizzo, RDI_OPEN.COMUNE_DESTINAZIONE as comune,
                   RDI_OPEN.PROVINCIA_DESTINAZIONE as provincia
            FROM MSSql32801.RDI_OPEN
            WHERE {whereClause}
            {orderBy}
            OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY";

        parameters["@offset"] = (req.Page - 1) * req.Size;
        parameters["@size"] = req.Size;

        await using var dataCmd = new SqlCommand(dataSql, conn) { CommandTimeout = 15 };
        foreach (var (k, v) in parameters)
            dataCmd.Parameters.AddWithValue(k, v ?? DBNull.Value);

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await dataCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        var totalPages = (int)Math.Ceiling((double)total / req.Size);

        sw.Stop();
        return Results.Json(new
        {
            success = true,
            data = rows,
            meta = new
            {
                page = req.Page,
                size = req.Size,
                total,
                total_pages = totalPages,
                has_next = req.Page < totalPages,
                has_previous = req.Page > 1
            },
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        sw.Stop();
        app.Logger.LogError(ex, "DB eventi/tecnico error");
        return Results.Json(new { 
            ok = false, 
            error = "DB_ERROR",
            error_code = "DB_ERROR",
            message = ex.Message, 
            durationMs = sw.ElapsedMilliseconds 
        }, statusCode: 500);
    }
});

// Login endpoint
app.MapPost("/db/auth/login", async (LoginReq req) =>
{
    var connStr = Environment.GetEnvironmentVariable("SQL_CONN") ?? DEFAULT_SQL_CONN;

    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Find user by username
        var userSql = @"
            SELECT ID as id, USERNAME as username, PASSWORD as password, role as ruolo, RAG_SOC as nome, email as email, block as attivo, TECNICO_ID as tecnico_id
            FROM MSSql32801.utenti
            WHERE USERNAME = @username";

        await using var userCmd = new SqlCommand(userSql, conn) { CommandTimeout = 15 };
        userCmd.Parameters.AddWithValue("@username", req.Username);

        await using var reader = await userCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            sw.Stop();
            return Results.Json(new { 
                ok = false, 
                error = "USER_NOT_FOUND",
                error_code = "USER_NOT_FOUND",
                message = "User not found",
                durationMs = sw.ElapsedMilliseconds 
            }, statusCode: 401);
        }

        var user = new Dictionary<string, object?>();
        for (int i = 0; i < reader.FieldCount; i++)
            user[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);

        // Verify password (plain text comparison for legacy database)
        var dbPassword = user["password"]?.ToString();
        if (dbPassword != req.Password)
        {
            sw.Stop();
            return Results.Json(new { 
                ok = false, 
                error = "INVALID_PASSWORD",
                error_code = "INVALID_PASSWORD",
                message = "Invalid password",
                durationMs = sw.ElapsedMilliseconds 
            }, statusCode: 401);
        }

        sw.Stop();
        return Results.Json(new
        {
            success = true,
            user,
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        sw.Stop();
        app.Logger.LogError(ex, "DB auth/login error");
        return Results.Json(new { 
            ok = false, 
            error = "DB_ERROR",
            error_code = "DB_ERROR",
            message = ex.Message, 
            durationMs = sw.ElapsedMilliseconds 
        }, statusCode: 500);
    }
});

// Get user by ID
app.MapGet("/db/users/{id}", async (string id) =>
{
    var connStr = Environment.GetEnvironmentVariable("SQL_CONN") ?? DEFAULT_SQL_CONN;

    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
            SELECT ID as id, USERNAME as username, PASSWORD as password, role as ruolo, RAG_SOC as nome, email as email, block as attivo, TECNICO_ID as tecnico_id
            FROM MSSql32801.utenti
            WHERE ID = @id";

        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@id", int.Parse(id));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            sw.Stop();
            return Results.Json(new { 
                ok = false, 
                error = "USER_NOT_FOUND",
                error_code = "USER_NOT_FOUND",
                message = "User not found",
                durationMs = sw.ElapsedMilliseconds 
            }, statusCode: 404);
        }

        var user = new Dictionary<string, object?>();
        for (int i = 0; i < reader.FieldCount; i++)
            user[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);

        sw.Stop();
        return Results.Json(new
        {
            success = true,
            data = user,
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        sw.Stop();
        app.Logger.LogError(ex, "DB users/{id} error", id);
        return Results.Json(new { 
            ok = false, 
            error = "DB_ERROR",
            error_code = "DB_ERROR",
            message = "Database error",
            durationMs = sw.ElapsedMilliseconds 
        }, statusCode: 500);
    }
});

// Get evento by ID
app.MapGet("/db/eventi/{id}", async (string id) =>
{
    var connStr = Environment.GetEnvironmentVariable("SQL_CONN") ?? DEFAULT_SQL_CONN;

    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
            SELECT RDI_OPEN.EVENTO_ID as evento_id, RDI_OPEN.WORKORDER as workorder, RDI_OPEN.DESCRIZIONE_EVENTO as descrizione_evento,
                   RDI_OPEN.STATO as stato, RDI_OPEN.ESITO_EVENTO as esito_evento, RDI_OPEN.UTENTE_INOLTRO as utente_inoltro,
                   RDI_OPEN.TECNICO_ID as tecnico_id, RDI_OPEN.DATA_RICHIESTA_CLI as data_richiesta_client,
                   RDI_OPEN.ORA_RICHIESTA_CLI as ora_richiesta_client, RDI_OPEN.DATA_MAX as data_max,
                   RDI_OPEN.ORA_MAX as ora_max, RDI_OPEN.DATA_PIANIFICA as data_pianifica, RDI_OPEN.PRIORITA as priorita,
                   RDI_OPEN.COD_TIPO_EVENTO as cod_tipo_evento, RDI_OPEN.DES_TIPO_EVENTO as des_tipo_evento,
                   RDI_OPEN.NOTE_EVENTO as note_evento, '' as nome_tecnico,
                   RDI_OPEN.DES_STATO as des_stato, RDI_OPEN.RAG_SOC_CF as cliente, RDI_OPEN.COD_CF as cod_cf, RDI_OPEN.CODCHI_E as codchi_e, RDI_OPEN.CONTATTO as contatto,
                   RDI_OPEN.TEL_CF as telefono, RDI_OPEN.TEL_CONTATTO as telefono_contatto,
                   RDI_OPEN.CELL_CONTATTO as cellulare, RDI_OPEN.DEVICE as device,
                   RDI_OPEN.MATRICOLA_DEVICE as matricola_device,
                   RDI_OPEN.INDI_DEST_MERCE as indirizzo, RDI_OPEN.COMUNE_DESTINAZIONE as comune,
                   RDI_OPEN.PROVINCIA_DESTINAZIONE as provincia
            FROM MSSql32801.RDI_OPEN
            WHERE RDI_OPEN.EVENTO_ID = @id";

        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@id", int.Parse(id));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            sw.Stop();
            return Results.Json(new { 
                ok = false, 
                error = "EVENTO_NOT_FOUND",
                error_code = "EVENTO_NOT_FOUND",
                message = "Event not found",
                durationMs = sw.ElapsedMilliseconds 
            }, statusCode: 404);
        }

        var evento = new Dictionary<string, object?>();
        for (int i = 0; i < reader.FieldCount; i++)
            evento[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);

        sw.Stop();
        return Results.Json(new
        {
            success = true,
            data = evento,
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        sw.Stop();
        app.Logger.LogError(ex, "DB eventi/{id} error", id);
        return Results.Json(new { 
            ok = false, 
            error = "DB_ERROR",
            error_code = "DB_ERROR",
            message = "Database error",
            durationMs = sw.ElapsedMilliseconds 
        }, statusCode: 500);
    }
});

// Update evento
app.MapPut("/db/eventi/{id}", async (string id, UpdateEventoReq req) =>
{
    var connStr = Environment.GetEnvironmentVariable("SQL_CONN") ?? DEFAULT_SQL_CONN;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var updateFields = new List<string>();
            var parameters = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(req.Stato)) { updateFields.Add("STATO = @stato"); parameters["@stato"] = req.Stato; }
            if (!string.IsNullOrEmpty(req.EsitoEvento)) { updateFields.Add("ESITO_EVENTO = @esito_evento"); parameters["@esito_evento"] = req.EsitoEvento; }
            if (!string.IsNullOrEmpty(req.TecnicoId)) { updateFields.Add("TECNICO_ID = @tecnico_id"); parameters["@tecnico_id"] = req.TecnicoId; }
            if (!string.IsNullOrEmpty(req.DataPianifica)) { updateFields.Add("DATA_PIANIFICA = @data_pianifica"); parameters["@data_pianifica"] = req.DataPianifica; }
            if (!string.IsNullOrEmpty(req.OraPianifica)) { updateFields.Add("ORA_PIANIFICA = @ora_pianifica"); parameters["@ora_pianifica"] = req.OraPianifica; }
            if (!string.IsNullOrEmpty(req.Priorita)) { updateFields.Add("PRIORITA = @priorita"); parameters["@priorita"] = req.Priorita; }
            if (!string.IsNullOrEmpty(req.NoteEvento)) { updateFields.Add("NOTE_EVENTO = @note_evento"); parameters["@note_evento"] = req.NoteEvento; }

            if (updateFields.Count == 0)
                return Results.Json(new { ok = false, error = "NO_FIELDS_TO_UPDATE", durationMs = sw.ElapsedMilliseconds }, statusCode: 400);

            var sql = $"UPDATE MSSql32801.RDI_OPEN SET {string.Join(", ", updateFields)} WHERE EVENTO_ID = @id";
            parameters["@id"] = int.Parse(id);

            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
            foreach (var (k, v) in parameters)
                cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);

            var affected = await cmd.ExecuteNonQueryAsync();

            sw.Stop();
            return Results.Json(new
            {
                success = true,
                affected,
                durationMs = sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            app.Logger.LogError(ex, "DB eventi/{id} update error", id);
            return Results.Json(new { ok = false, error = "DB_ERROR", durationMs = sw.ElapsedMilliseconds }, statusCode: 500);
        }
    });
    // Update intervento
    app.MapPut("/db/interventi/{eventoId:int}", async (int eventoId, CreateInterventoReq req) =>
    {
        var connStr = Environment.GetEnvironmentVariable("SQL_CONN") ?? DEFAULT_SQL_CONN;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Ottieni il WORKORDER dell'intervento per aggiornare RDI_OPEN dopo
            var getWorkorderSql = "SELECT WORKORDER FROM MSSql32801.INT_OPEN WHERE EVENTO_ID = @eventoId";
            string? workorder = null;
            
            await using (var workorderCmd = new SqlCommand(getWorkorderSql, conn) { CommandTimeout = 15 })
            {
                workorderCmd.Parameters.AddWithValue("@eventoId", eventoId);
                workorder = (await workorderCmd.ExecuteScalarAsync()) as string;
            }

            // UPDATE intervento
            var sql = @"
                UPDATE MSSql32801.INT_OPEN
                SET COD_TIPO_EVENTO = @codTipoEvento,
                    DES_TIPO_EVENTO = @desTipoEvento,
                    DESCRIZIONE_EVENTO = @descrizioneEvento,
                    STATO = @stato,
                    DES_STATO = @desStato,
                    ORA_ARRIVO = @oraArrivo,
                    ORA_CHIUSURA = @oraChiusura,
                    ORE_VIAGGIO = @oreViaggio,
                    ORE_IMPEGNATE = @oreImpegnate,
                    TEMPO_INTERVENTO = @tempoIntervento
                WHERE EVENTO_ID = @eventoId";

            // Calcola TEMPO_INTERVENTO come differenza tra ORA_CHIUSURA e ORA_ARRIVO
            DateTime? tempoIntervento = null;
            if (req.OraChiusura != null && req.OraArrivo != null)
            {
                try
                {
                    var chiusura = DateTime.Parse(req.OraChiusura);
                    var arrivo = DateTime.Parse(req.OraArrivo);
                    var diff = chiusura - arrivo;
                    
                    // Usa la data di chiusura con le ore/minuti della differenza
                    tempoIntervento = chiusura.Date.AddHours(diff.Hours).AddMinutes(diff.Minutes);
                }
                catch
                {
                    // In caso di errore nel parsing, lascia null
                }
            }

            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
            cmd.Parameters.AddWithValue("@eventoId", eventoId);
            cmd.Parameters.AddWithValue("@codTipoEvento", req.TipoIntervento ?? "");
            cmd.Parameters.AddWithValue("@desTipoEvento", req.DescrizioneTipo ?? "");
            cmd.Parameters.AddWithValue("@descrizioneEvento", req.DescrizioneIntervento ?? "");
            cmd.Parameters.AddWithValue("@stato", req.StatoIntervento ?? "01");
            cmd.Parameters.AddWithValue("@desStato", req.DescrizioneStato ?? "");
            cmd.Parameters.AddWithValue("@oraArrivo", (object?)req.OraArrivo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@oraChiusura", (object?)req.OraChiusura ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@oreViaggio", (object?)req.OreViaggio ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@oreImpegnate", (object?)req.OreImpegnate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tempoIntervento", (object?)tempoIntervento ?? DBNull.Value);

            var affected = await cmd.ExecuteNonQueryAsync();

            // Aggiorna RDI_OPEN: svuota DATA_PIANIFICA e imposta ESITO_EVENTO se stato richiede attachment
            if (!string.IsNullOrEmpty(workorder))
            {
                var stati_chiusura = new[] { "03", "06", "15", "16" };
                var requiresAttachment = stati_chiusura.Contains(req.StatoIntervento ?? "");
                
                var updateRdiSql = requiresAttachment 
                    ? "UPDATE MSSql32801.RDI_OPEN SET ESITO_EVENTO = 1, DATA_PIANIFICA = NULL WHERE WORKORDER = @workorder"
                    : "UPDATE MSSql32801.RDI_OPEN SET DATA_PIANIFICA = NULL WHERE WORKORDER = @workorder";
                
                await using (var updateRdiCmd = new SqlCommand(updateRdiSql, conn) { CommandTimeout = 15 })
                {
                    updateRdiCmd.Parameters.AddWithValue("@workorder", workorder);
                    await updateRdiCmd.ExecuteNonQueryAsync();
                }
            }

            sw.Stop();
            return Results.Json(new
            {
                success = true,
                affected,
                durationMs = sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            app.Logger.LogError(ex, "DB interventi update error");
            return Results.Json(new { 
                ok = false, 
                error = "DB_ERROR",
                error_code = "DB_ERROR",
                message = "Database error",
                durationMs = sw.ElapsedMilliseconds 
            }, statusCode: 500);
        }
    });
    // Create intervento
    app.MapPost("/db/interventi", async (CreateInterventoReq req) =>
    {
        var connStr = Environment.GetEnvironmentVariable("SQL_CONN") ?? DEFAULT_SQL_CONN;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Get the WORKORDER from the parent evento (RDI_OPEN table)
            var getWorkorderSql = "SELECT WORKORDER FROM MSSql32801.RDI_OPEN WHERE EVENTO_ID = @eventoId";
            string? workorder = null;
            
            await using (var workorderCmd = new SqlCommand(getWorkorderSql, conn) { CommandTimeout = 15 })
            {
                workorderCmd.Parameters.AddWithValue("@eventoId", req.EventoId);
                workorder = (await workorderCmd.ExecuteScalarAsync()) as string;
            }
            
            if (string.IsNullOrEmpty(workorder))
            {
                sw.Stop();
                return Results.Json(new { 
                    ok = false, 
                    error = "EVENTO_NOT_FOUND",
                    error_code = "EVENTO_NOT_FOUND",
                    message = "Event not found",
                    durationMs = sw.ElapsedMilliseconds 
                }, statusCode: 404);
            }

            // Generate a new unique EVENTO_ID (get max + 1)
            var getMaxEventoIdSql = "SELECT ISNULL(MAX(EVENTO_ID), 0) + 1 FROM MSSql32801.INT_OPEN";
            int newEventoId = 1;
            
            await using (var maxCmd = new SqlCommand(getMaxEventoIdSql, conn) { CommandTimeout = 15 })
            {
                var result = await maxCmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    newEventoId = Convert.ToInt32(result);
                }
            }

            // INSERT with generated EVENTO_ID and parent's WORKORDER
            var sql = @"
                INSERT INTO MSSql32801.INT_OPEN (EVENTO_ID, WORKORDER, TECNICO_ID, COD_TIPO_EVENTO, DES_TIPO_EVENTO,
                                                DESCRIZIONE_EVENTO, STATO, DES_STATO, ESITO_EVENTO, DATA_EVENTO,
                                                ORA_ARRIVO, ORA_CHIUSURA, ORE_VIAGGIO, ORE_IMPEGNATE, TEMPO_INTERVENTO)
                VALUES (@eventoId, @workorder, @tecnicoId, @codTipoEvento, @desTipoEvento,
                        @descrizioneEvento, @stato, @desStato, 0, @dataEvento,
                        @oraArrivo, @oraChiusura, @oreViaggio, @oreImpegnate, @tempoIntervento)";

            // Calcola TEMPO_INTERVENTO come differenza tra ORA_CHIUSURA e ORA_ARRIVO
            DateTime? tempoIntervento = null;
            if (req.OraChiusura != null && req.OraArrivo != null)
            {
                try
                {
                    var chiusura = DateTime.Parse(req.OraChiusura);
                    var arrivo = DateTime.Parse(req.OraArrivo);
                    var diff = chiusura - arrivo;
                    
                    // Usa la data di chiusura con le ore/minuti della differenza
                    tempoIntervento = chiusura.Date.AddHours(diff.Hours).AddMinutes(diff.Minutes);
                }
                catch
                {
                    // In caso di errore nel parsing, lascia null
                }
            }

            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
            cmd.Parameters.AddWithValue("@eventoId", newEventoId);
            cmd.Parameters.AddWithValue("@workorder", workorder);
            cmd.Parameters.AddWithValue("@tecnicoId", req.TecnicoId ?? "");
            cmd.Parameters.AddWithValue("@codTipoEvento", req.TipoIntervento ?? "");
            cmd.Parameters.AddWithValue("@desTipoEvento", req.DescrizioneTipo ?? "");
            cmd.Parameters.AddWithValue("@descrizioneEvento", req.DescrizioneIntervento ?? "");
            cmd.Parameters.AddWithValue("@stato", req.StatoIntervento ?? "01");
            cmd.Parameters.AddWithValue("@desStato", req.DescrizioneStato ?? "");
            cmd.Parameters.AddWithValue("@dataEvento", req.DataIntervento ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@oraArrivo", (object?)req.OraArrivo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@oraChiusura", (object?)req.OraChiusura ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@oreViaggio", (object?)req.OreViaggio ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@oreImpegnate", (object?)req.OreImpegnate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tempoIntervento", (object?)tempoIntervento ?? DBNull.Value);

            var affected = await cmd.ExecuteNonQueryAsync();

            // Aggiorna RDI_OPEN: svuota DATA_PIANIFICA e imposta ESITO_EVENTO se stato richiede attachment
            var stati_chiusura = new[] { "03", "06", "15", "16" };
            var requiresAttachment = stati_chiusura.Contains(req.StatoIntervento ?? "");
            
            var updateRdiSql = requiresAttachment 
                ? "UPDATE MSSql32801.RDI_OPEN SET ESITO_EVENTO = 1, DATA_PIANIFICA = NULL WHERE WORKORDER = @workorder"
                : "UPDATE MSSql32801.RDI_OPEN SET DATA_PIANIFICA = NULL WHERE WORKORDER = @workorder";
            
            await using (var updateRdiCmd = new SqlCommand(updateRdiSql, conn) { CommandTimeout = 15 })
            {
                updateRdiCmd.Parameters.AddWithValue("@workorder", workorder);
                await updateRdiCmd.ExecuteNonQueryAsync();
            }

            sw.Stop();
            return Results.Json(new
            {
                success = true,
                affected,
                durationMs = sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            app.Logger.LogError(ex, "DB interventi create error");
            return Results.Json(new { 
                ok = false, 
                error = "DB_ERROR",
                error_code = "DB_ERROR",
                message = "Database error",
                durationMs = sw.ElapsedMilliseconds 
            }, statusCode: 500);
        }
    });

// Get interventi by evento ID
app.MapGet("/db/eventi/{eventoId}/interventi", async (string eventoId, int page = 1, int size = 50) =>
{
    var connStr = Environment.GetEnvironmentVariable("SQL_CONN") ?? DEFAULT_SQL_CONN;

    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // First, get the WORKORDER of the parent evento
        var getWorkorderSql = "SELECT WORKORDER FROM MSSql32801.RDI_OPEN WHERE EVENTO_ID = @eventoId";
        string? workorder = null;
        
        await using (var workorderCmd = new SqlCommand(getWorkorderSql, conn) { CommandTimeout = 15 })
        {
            workorderCmd.Parameters.AddWithValue("@eventoId", int.Parse(eventoId));
            workorder = (await workorderCmd.ExecuteScalarAsync()) as string;
        }
        
        if (string.IsNullOrEmpty(workorder))
        {
            // Evento not found or has no workorder, return empty result
            sw.Stop();
            return Results.Json(new
            {
                success = true,
                data = new List<object>(),
                meta = new
                {
                    page,
                    size,
                    total = 0,
                    total_pages = 0,
                    has_next = false,
                    has_previous = false
                },
                durationMs = sw.ElapsedMilliseconds
            });
        }

        // Count query - filter by WORKORDER
        var countSql = "SELECT COUNT(*) as total_count FROM MSSql32801.INT_OPEN WHERE WORKORDER = @workorder";

        var total = 0;
        try
        {
            await using var countCmd = new SqlCommand(countSql, conn) { CommandTimeout = 15 };
            countCmd.Parameters.AddWithValue("@workorder", workorder);
            total = (int)await countCmd.ExecuteScalarAsync();
        }
        catch
        {
            // Table might not exist, return empty result
            total = 0;
        }

        var rows = new List<Dictionary<string, object?>>();
        try
        {
            // Data query - filter by WORKORDER
            var dataSql = @"
                SELECT *
                FROM MSSql32801.INT_OPEN
                WHERE WORKORDER = @workorder
                ORDER BY DATA_EVENTO DESC
                OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY";

            await using var dataCmd = new SqlCommand(dataSql, conn) { CommandTimeout = 15 };
            dataCmd.Parameters.AddWithValue("@workorder", workorder);
            dataCmd.Parameters.AddWithValue("@offset", (page - 1) * size);
            dataCmd.Parameters.AddWithValue("@size", size);

            await using var reader = await dataCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
        }
        catch
        {
            // Table might not exist, return empty result
            rows = new List<Dictionary<string, object?>>();
        }

        var totalPages = (int)Math.Ceiling((double)total / size);

        sw.Stop();
        return Results.Json(new
        {
            success = true,
            data = rows,
            meta = new
            {
                page,
                size,
                total,
                total_pages = totalPages,
                has_next = page < totalPages,
                has_previous = page > 1
            },
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        sw.Stop();
        app.Logger.LogError(ex, "DB eventi/{eventoId}/interventi error", eventoId);
        return Results.Json(new { 
            ok = false, 
            error = "DB_ERROR",
            error_code = "DB_ERROR",
            message = "Database error",
            durationMs = sw.ElapsedMilliseconds 
        }, statusCode: 500);
    }
});

// Get tipi intervento
app.MapGet("/db/interventi/tipi", async () =>
{
    var connStr = Environment.GetEnvironmentVariable("SQL_CONN") ?? DEFAULT_SQL_CONN;

    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

                var rows = new List<Dictionary<string, object?>>();
        try
        {
            var sql = "SELECT COD_TIPO_EVENTO, DES_TIPO_EVENTO, LIV_1_TIPO_EVENTO FROM MSSql32801.TIPI_EVENTO WHERE DES_TIPO_EVENTO IS NOT NULL ORDER BY COD_TIPO_EVENTO";

            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                    // Fix encoding for string values from SQL Server
                    if (value is string strValue)
                    {
                        value = FixEncoding(strValue);
                    }
                    row[reader.GetName(i)] = value;
                }
                rows.Add(row);
            }
        }
        catch
        {
            // Table might not exist, return empty result
            rows = new List<Dictionary<string, object?>>();
        }

        sw.Stop();
        return Results.Json(new
        {
            success = true,
            data = rows,
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        sw.Stop();
        app.Logger.LogError(ex, "DB interventi/tipi error");
        return Results.Json(new { 
            ok = false, 
            error = "DB_ERROR",
            error_code = "DB_ERROR",
            message = "Database error",
            durationMs = sw.ElapsedMilliseconds 
        }, statusCode: 500);
    }
});

// Get stati intervento
app.MapGet("/db/interventi/stati", async () =>
{
    var connStr = Environment.GetEnvironmentVariable("SQL_CONN") ?? DEFAULT_SQL_CONN;

    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

                var rows = new List<Dictionary<string, object?>>();
        try
        {
            var sql = "SELECT STATO, DES_STATO FROM MSSql32801.TEK_STATI_EVENTO WHERE DES_STATO IS NOT NULL ORDER BY STATO";

            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                    // Fix encoding for string values from SQL Server
                    if (value is string strValue)
                    {
                        value = FixEncoding(strValue);
                    }
                    row[reader.GetName(i)] = value;
                }
                rows.Add(row);
            }
        }
        catch
        {
            // Table might not exist, return empty result
            rows = new List<Dictionary<string, object?>>();
        }

        sw.Stop();
        return Results.Json(new
        {
            success = true,
            data = rows,
            durationMs = sw.ElapsedMilliseconds
        });
    }
    catch (Exception ex)
    {
        sw.Stop();
        app.Logger.LogError(ex, "DB interventi/stati error");
        return Results.Json(new { 
            ok = false, 
            error = "DB_ERROR",
            error_code = "DB_ERROR",
            message = "Database error",
            durationMs = sw.ElapsedMilliseconds 
        }, statusCode: 500);
    }
});

// Helper function to fix encoding issues from SQL Server
// The � character (U+FFFD) appears when SQL Server data is read with wrong encoding
static string FixEncoding(string text)
{
    if (string.IsNullOrEmpty(text))
        return text;
    
    // Manual character replacement for common Italian accented characters
    // This is the most reliable solution when data is already corrupted
    var result = text
        .Replace("Attivit�", "Attività")
        .Replace("attivit�", "attività")
        .Replace("citt�", "città")
        .Replace("qualit�", "qualità")
        .Replace("possibilit�", "possibilità")
        .Replace("met�", "metà")
        .Replace("universit�", "università");
    
    // Generic replacements for remaining cases
    if (result.Contains("�"))
    {
        // If still contains �, try generic vowel replacements
        // Note: This is a fallback and might not always be accurate
        result = result
            .Replace("�", "à"); // Most common case
    }
    
    return result;
}

app.Run();

// DTO
public record EventiByTecnicoReq(
    string TecnicoId,
    int Page = 1,
    int Size = 50,
    string? IsPlanned = null,
    string? Search = null,
    string? Cliente = null,
    string? Comune = null,
    string? Provincia = null,
    string? DateFrom = null,
    string? DateTo = null,
    string? Priority = null,
    string[]? Statuses = null,
    string? Sort = null
);
public record LoginReq(string Username, string Password);
public record UpdateEventoReq(
    string? Stato = null,
    string? EsitoEvento = null,
    string? TecnicoId = null,
    string? DataPianifica = null,
    string? OraPianifica = null,
    string? Priorita = null,
    string? NoteEvento = null
);
public record CreateInterventoReq(
    int EventoId,
    string? Workorder = null,
    string? TecnicoId = null,
    string? TipoIntervento = null,
    string? DescrizioneTipo = null,
    string? DescrizioneIntervento = null,
    string? StatoIntervento = null,
    string? DescrizioneStato = null,
    string? DataIntervento = null,
    string? OraArrivo = null,
    string? OraChiusura = null,
    string? OreViaggio = null,
    string? OreImpegnate = null
);
