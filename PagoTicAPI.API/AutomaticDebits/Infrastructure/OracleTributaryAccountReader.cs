using System.Data;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.Utilities;
using PagoTicAPI.Domain.Context;

namespace PagoTicAPI.API.AutomaticDebits.Infrastructure;

public sealed class OracleTributaryAccountReader : ITributaryAccountReader
{
    private const string Query = """
        SELECT tc.ID_JURISDICCION,
               tc.ID_TRIBUTO_CONTRIBUYENTE,
               tc.ID_PERSONA,
               TO_CHAR(tc.ID_TIPO_TRIBUTO),
               tt.CONCEPTO,
               tc.CLAVE_BIEN,
               TRIM(p.NOMBRE || ' ' || p.APELLIDO),
               COALESCE(tc.EMAIL, p.EMAIL),
               p.CUIL,
               p.DOCUMENTO_NRO
          FROM INGRESOS.T_TRIBUTOS_CONTRIBUYENTES tc
          JOIN PORTAL.T_PERSONAS p
            ON p.ID_PERSONA = tc.ID_PERSONA
          JOIN INGRESOS.T_TIPOS_TRIBUTOS tt
            ON tt.ID_TIPO_TIBUTO = tc.ID_TIPO_TRIBUTO
         WHERE tc.ID_JURISDICCION = :jurisdictionId
           AND tc.ID_TRIBUTO_CONTRIBUYENTE = :taxpayerAccountId
           AND tc.ACTIVO = '1'
           AND tc.FEC_BAJA IS NULL
           AND p.FEC_BAJA IS NULL
        """;

    private readonly gtwContext _context;

    public OracleTributaryAccountReader(gtwContext context)
    {
        _context = context;
    }

    public async Task<TributaryAccountReadModel?> GetByAccountAsync(
        long jurisdictionId,
        string taxpayerAccountId,
        CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        if (mustClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Query;
            if (command is OracleCommand oracleCommand)
            {
                oracleCommand.BindByName = true;
            }

            AddParameter(command, "jurisdictionId", DbType.Int64, jurisdictionId);
            AddParameter(command, "taxpayerAccountId", DbType.String, taxpayerAccountId);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var identification = PayPerTicIdentificationMapper.Map(
                NullableString(reader, 8),
                NullableString(reader, 9));
            return new TributaryAccountReadModel(
                Convert.ToInt64(reader.GetValue(0)),
                RequiredString(reader, 1),
                RequiredString(reader, 2),
                RequiredString(reader, 3),
                RequiredString(reader, 4),
                RequiredString(reader, 5),
                RequiredString(reader, 6),
                RequiredString(reader, 7),
                identification.Type,
                identification.Number,
                identification.Country);
        }
        finally
        {
            if (mustClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        DbType type,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string RequiredString(System.Data.Common.DbDataReader reader, int ordinal) =>
        NullableString(reader, ordinal) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException("The tributary account has incomplete PayPerTIC payer data.");

    private static string? NullableString(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal))?.Trim();
}

