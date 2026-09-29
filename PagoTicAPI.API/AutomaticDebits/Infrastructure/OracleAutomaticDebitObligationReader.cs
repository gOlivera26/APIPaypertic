using System.Data;
using Microsoft.EntityFrameworkCore;
using PagoTicAPI.Application.Services.Interfaces;

namespace PagoTicAPI.API.AutomaticDebits.Infrastructure;

public sealed class OracleAutomaticDebitObligationReader : IAutomaticDebitObligationReader
{
    private readonly gtwContext _context;

    public OracleAutomaticDebitObligationReader(gtwContext context) => _context = context;

    public async Task<AutomaticDebitObligationSnapshot?> FindAsync(
        long jurisdictionId,
        string obligationId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT o.ID_OBLIGACION,
                   o.ID_JURISDICCION,
                   o.ID_TRIBUTO_CONTRIBUYENTE,
                   o.ID_TIPOS_TRIBUTOS,
                   tt.CONCEPTO,
                   o.NRO_CUOTA,
                   o.ANO_CUOTA,
                   o.FECHA_PRIMER_VENCIMIENTO,
                   o.ESTADO_DEUDA,
                   o.SITUACION_DEUDA,
                   CASE WHEN o.FEC_BAJA IS NULL THEN 0 ELSE 1 END ES_BAJA,
                   SUM(od.MONTO_ITEM) IMPORTE,
                   INGRESOS.F_CALCULA_COBRADOPARCIAL(o.ID_OBLIGACION, 'T') COBRADO_PARCIAL
              FROM INGRESOS.T_OBLIGACIONES o
              JOIN INGRESOS.T_OBLIGACIONES_DETALLE od ON od.ID_OBLIGACION = o.ID_OBLIGACION
              JOIN INGRESOS.T_TIPOS_TRIBUTOS tt ON tt.ID_TIPO_TIBUTO = o.ID_TIPOS_TRIBUTOS
             WHERE o.ID_JURISDICCION = :idJurisdiccion
               AND o.ID_OBLIGACION = :idObligacion
             GROUP BY o.ID_OBLIGACION, o.ID_JURISDICCION, o.ID_TRIBUTO_CONTRIBUYENTE,
                      o.ID_TIPOS_TRIBUTOS, tt.CONCEPTO, o.NRO_CUOTA, o.ANO_CUOTA,
                      o.FECHA_PRIMER_VENCIMIENTO, o.ESTADO_DEUDA, o.SITUACION_DEUDA,
                      o.FEC_BAJA
            """;

        var connection = _context.Database.GetDbConnection();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "idJurisdiccion", jurisdictionId);
            AddParameter(command, "idObligacion", obligationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            return new AutomaticDebitObligationSnapshot(
                reader.GetValue(0).ToString()!, Convert.ToInt64(reader.GetValue(1)), reader.GetString(2),
                reader.GetValue(3).ToString()!, reader.GetString(4), reader.GetString(5), reader.GetValue(6).ToString()!,
                reader.GetDateTime(7), reader.GetString(8), reader.GetString(9), Convert.ToInt32(reader.GetValue(10)) == 1,
                Convert.ToDecimal(reader.GetValue(11)), reader.IsDBNull(12) ? 0 : Convert.ToDecimal(reader.GetValue(12)));
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

