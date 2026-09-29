using System.Data;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.Webhooks.AutomaticDebits;
using PagoTicAPI.Domain.Context;
using PagoTicAPI.Domain.Context.Configuration;

namespace PagoTicAPI.API.AutomaticDebits.Infrastructure;

public sealed class OracleAutomaticDebitIdGenerator :
    IAutomaticDebitIdGenerator,
    IAutomaticDebitWebhookIdGenerator
{
    private const string Schema = "GATEWAY";
    private const string AdhesionSequence = $"{Schema}.{AutomaticDebitDatabaseNames.Sequences.Adhesions}";
    private const string OperationSequence = $"{Schema}.{AutomaticDebitDatabaseNames.Sequences.Operations}";
    private const string WebhookInboxSequence = $"{Schema}.{AutomaticDebitDatabaseNames.Sequences.Notifications}";
    private const string EventSequence = $"{Schema}.{AutomaticDebitDatabaseNames.Sequences.Events}";
    private readonly gtwContext _context;

    public OracleAutomaticDebitIdGenerator(gtwContext context)
    {
        _context = context;
    }

    public Task<long> NextAdhesionIdAsync(CancellationToken cancellationToken = default) =>
        NextValueAsync(AdhesionSequence, cancellationToken);

    public Task<long> NextOperationIdAsync(CancellationToken cancellationToken = default) =>
        NextValueAsync(OperationSequence, cancellationToken);

    public Task<long> NextInboxIdAsync(CancellationToken cancellationToken = default) =>
        NextValueAsync(WebhookInboxSequence, cancellationToken);

    public Task<long> NextEventIdAsync(CancellationToken cancellationToken = default) =>
        NextValueAsync(EventSequence, cancellationToken);

    private async Task<long> NextValueAsync(string sequence, CancellationToken cancellationToken)
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
            command.CommandText = $"SELECT {sequence}.NEXTVAL FROM DUAL";
            if (command is OracleCommand oracleCommand)
            {
                oracleCommand.BindByName = true;
            }

            var value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(value);
        }
        finally
        {
            if (mustClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}

