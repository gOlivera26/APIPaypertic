using Microsoft.EntityFrameworkCore;
using PagoTicAPI.Domain.Context;

namespace PagoTicAPI.API.AutomaticDebits.Scheduling;

public sealed class OracleAutomaticDebitSchedulerLease : IAutomaticDebitSchedulerLease
{
    private readonly gtwContext _context;

    public OracleAutomaticDebitSchedulerLease(gtwContext context) => _context = context;

    public async Task<bool> TryAcquireAsync(
        string leaseName,
        string owner,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var seconds = checked((int)Math.Ceiling(duration.TotalSeconds));
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var updated = await _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE GATEWAY.T_DEB_AUT_SCHEDULER_LEASE
               SET PROPIETARIO = {owner},
                   FECHA_ADQUISICION = SYSTIMESTAMP,
                   FECHA_EXPIRACION = SYSTIMESTAMP + NUMTODSINTERVAL({seconds}, 'SECOND')
             WHERE NOMBRE_LEASE = {leaseName}
               AND (FECHA_EXPIRACION <= SYSTIMESTAMP OR PROPIETARIO = {owner})",
            cancellationToken);

        if (updated == 1)
        {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        try
        {
            var inserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO GATEWAY.T_DEB_AUT_SCHEDULER_LEASE
                    (ID_LEASE, NOMBRE_LEASE, PROPIETARIO, FECHA_ADQUISICION, FECHA_EXPIRACION)
                VALUES
                    (GATEWAY.SQ_DEB_AUT_SCHED_LEASE.NEXTVAL, {leaseName}, {owner},
                     SYSTIMESTAMP, SYSTIMESTAMP + NUMTODSINTERVAL({seconds}, 'SECOND'))",
                cancellationToken);
            if (inserted == 1)
            {
                await transaction.CommitAsync(cancellationToken);
                return true;
            }

            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return false;
        }
        catch (Exception exception) when (IsOracleUniqueConstraint(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return false;
        }
    }

    public async Task ReleaseAsync(
        string leaseName,
        string owner,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE GATEWAY.T_DEB_AUT_SCHEDULER_LEASE
               SET FECHA_EXPIRACION = SYSTIMESTAMP
             WHERE NOMBRE_LEASE = {leaseName}
               AND PROPIETARIO = {owner}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsOracleUniqueConstraint(Exception exception) =>
        exception.Message.Contains("ORA-00001", StringComparison.OrdinalIgnoreCase);
}

