namespace PagoTicAPI.Application.Services.Implementations;

/// <summary>
/// Implementación del servicio PayPerTIC.
///
/// Flujo de CrearCheckout:
///   1. Guarda GtwPago (estado: "pending") + GtwPagoDetalles en BD.
///   2. Llama a Oracle (p_genera_comprobante) para registrar el cedulón.
///   3. Llama a IPayPerTicClient.CreatePaymentAsync().
///   4. Actualiza PagIdExterno con el ID de PayPerTIC y guarda.
///
/// Flujo de Webhook:
///   1. Busca GtwPago por PagIdExterno.
///   2. Mapea el estado de PayPerTIC al estado interno (vía Enum).
///   3. Actualiza PagEstado, PagFormaPago y PagMetodoPago.
/// </summary>
public class PayPerTicService : BaseService, IPayPerTicService
{
    private readonly gtwContext _gtwContext;
    private readonly IPayPerTicClient _payPerTicClient;
    private readonly PayPerTicKeys _keys;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PayPerTicService> _logger;
    private readonly IAutomaticDebitPayPerTicClient _automaticDebitClient;
    private readonly IJurisdictionPayPerTicConfigurationProvider _configurationProvider;

    public PayPerTicService(
        gtwContext context,
        IPayPerTicClient payPerTicClient,
        IOptions<PayPerTicKeys> keys,
        TimeProvider timeProvider,
        ILogger<PayPerTicService> logger,
        IAutomaticDebitPayPerTicClient automaticDebitClient,
        IJurisdictionPayPerTicConfigurationProvider configurationProvider)
    {
        _gtwContext = context;
        _payPerTicClient = payPerTicClient;
        _keys = keys.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _automaticDebitClient = automaticDebitClient;
        _configurationProvider = configurationProvider;
    }

    public async Task<OperationResponse<PayPerTicCheckoutResultDto>> CrearCheckoutAsync(
        CreatePayPerTicCheckoutDto request)
    {
        try
        {
            var externalTransactionId = !string.IsNullOrWhiteSpace(request.ExternalTransactionId)
                ? request.ExternalTransactionId
                : $"PPT-{Guid.NewGuid():N}";

            var totalAmount = request.Details.Sum(d => d.Amount);

            int pagId = await ObtenerSecuenciaAsync("GATEWAY.SEQ_PAGO");

            var pago = new GtwPago
            {
                PagId = pagId,
                PagFecha = _timeProvider.GetUtcNow().UtcDateTime,
                PagOrigen = request.Origen,
                PagModoPago = "PayPerTIC",     // va a ser interpretado como Ente id = 9 en bd
                PagFormaPago = string.IsNullOrWhiteSpace(request.Type) ? "PayPerTIC" : request.Type,
                PagMetodoPago = "PayPerTIC",
                PagEstado = "pending",
                PagEstadoDetalle = $"Pago local preparado. ExternalTransactionId: {externalTransactionId}",
                PagMoneda = request.CurrencyId,
                PagIdExterno = null,
                PagImporteAbonado = totalAmount,
                PagImporteCancelado = totalAmount,
                PagIdJurisdiccion = request.JurisdiccionId
            };

            var detalles = new List<GtwPagoDetalle>();

            foreach (var d in request.Details)
            {
                detalles.Add(new GtwPagoDetalle
                {
                    PgdId = await ObtenerSecuenciaAsync("GATEWAY.SEQ_PAGO_DETALLE"),
                    PgdConcepto = string.IsNullOrWhiteSpace(d.ConceptDescription) ? "SD" : d.ConceptDescription,
                    PgdIdTributoContribuyente = string.IsNullOrWhiteSpace(d.IdTributoContribuyente) ? "SD" : d.IdTributoContribuyente,
                    PgdClaveBien = string.IsNullOrWhiteSpace(d.ClaveBien) ? "SD" : d.ClaveBien,
                    PgdContribuyente = d.Contribuyente,
                    PgdIdObligacion = string.IsNullOrWhiteSpace(d.IdObligacion) ? "SD" : d.IdObligacion,
                    PgdAnoCuota = d.AnoCuota,
                    PgdNroCuota = d.NroCuota,
                    PgdCapitalFacturado = d.CapitalFacturado,
                    PgdIntereses = d.Intereses,
                    PgdDeudaActualizada = d.DeudaActualizada,
                    PgdTipoCuota = d.TipoCuota,
                    PgdTipoTributo = d.TipoTributo
                });
            }

            pago.GtwPagoDetalles = detalles;

            var payPerTicRequest = BuildCreatePaymentRequest(request, externalTransactionId);
            var transactionResult = await InsertWithTransactionAsync(async () =>
            {
                // Guardamos el pago y los detalles iniciales
                _gtwContext.GtwPagos.Add(pago);
                await _gtwContext.SaveChangesAsync();

                // Ejecutamos el Procedure para generar el comprobante
                await GeneraComprobanteAsync(pago.PagId);

            }, _gtwContext);

            if (transactionResult.Success != true)
            {
                return InternalServerError<PayPerTicCheckoutResultDto>(
                    "No fue posible preparar el pago.");
            }

            PayPerTicCreatePaymentResponse payPerTicResponse;
            try
            {
                // La llamada remota se realiza fuera de la transacción Oracle para no mantener
                // bloqueos mientras se espera a un sistema externo.
                payPerTicResponse = await _payPerTicClient.CreatePaymentAsync(payPerTicRequest);
            }
            catch (Exception exception)
            {
                pago.PagEstadoDetalle =
                    $"Resultado PayPerTIC incierto. ExternalTransactionId: {externalTransactionId}";
                await _gtwContext.SaveChangesAsync();
                _logger.LogWarning(
                    exception,
                    "La creación del checkout en PayPerTIC falló luego de preparar el pago local. PagId={PagId} ExternalTransactionId={ExternalTransactionId}",
                    pago.PagId,
                    externalTransactionId);
                return InternalServerError<PayPerTicCheckoutResultDto>(
                    "No fue posible confirmar la creación del pago en PayPerTIC.");
            }

            pago.PagIdExterno = payPerTicResponse.Id;
            pago.PagEstadoDetalle = $"Pago creado en PayPerTIC. Status: {payPerTicResponse.Status}";
            await _gtwContext.SaveChangesAsync();

            return OperationResponse<PayPerTicCheckoutResultDto>.SuccessResponse(new PayPerTicCheckoutResultDto
            {
                IsSuccess = true,
                PaymentId = payPerTicResponse!.Id,
                FormUrl = payPerTicResponse.FormUrl,
                FinalAmount = payPerTicResponse.FinalAmount,
                Status = payPerTicResponse.Status,
                ExternalTransactionId = externalTransactionId,
                LocalPagId = pago.PagId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error inesperado al crear el checkout de PayPerTIC.");
            return InternalServerError<PayPerTicCheckoutResultDto>("No fue posible crear el pago.");
        }
    }

    public async Task<OperationResponse<bool>> RecibirWebhookAsync(
        PayPerTicWebhookNotification notification)
    {
        PayPerTicPaymentWebhookInbox? inbox = null;
        try
        {
            if (string.IsNullOrWhiteSpace(notification.Id))
                return BadRequest<bool>("El campo 'id' del webhook es requerido.");

            var pago = await _gtwContext.GtwPagos
                .FirstOrDefaultAsync(p => p.PagIdExterno == notification.Id);

            if (pago is null)
                return NotFound<bool>();

            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(notification);
            var payloadHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(payloadBytes))
                .ToLowerInvariant();
            var deduplicationKey = CreateWebhookDeduplicationKey(
                pago.PagIdJurisdiccion,
                notification.Id,
                payloadHash);

            inbox = await _gtwContext.PayPerTicPaymentWebhookInbox
                .FirstOrDefaultAsync(x =>
                    x.JurisdictionId == pago.PagIdJurisdiccion &&
                    x.DeduplicationKey == deduplicationKey);

            if (inbox?.Result == PayPerTicPaymentWebhookInboxResults.Processed)
            {
                return OperationResponse<bool>.SuccessResponse(true);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (inbox is null)
            {
                inbox = new PayPerTicPaymentWebhookInbox
                {
                    Id = await ObtenerSecuenciaAsync("GATEWAY.SQ_PAGO_PPT_NOTIFICACIONES"),
                    JurisdictionId = pago.PagIdJurisdiccion,
                    PaymentId = pago.PagId,
                    ProviderPaymentId = notification.Id,
                    DeduplicationKey = deduplicationKey,
                    PayloadHash = payloadHash,
                    Result = PayPerTicPaymentWebhookInboxResults.Received,
                    ReceivedAt = now
                };
                _gtwContext.PayPerTicPaymentWebhookInbox.Add(inbox);
            }

            inbox.Attempts++;
            inbox.LastAttemptAt = now;
            inbox.FailureCode = null;
            await _gtwContext.SaveChangesAsync();

            var configuration = await _configurationProvider.GetActiveAsync(pago.PagIdJurisdiccion);
            var readBack = await _automaticDebitClient.GetPaymentReadBackAsync(
                pago.PagIdJurisdiccion,
                notification.Id);
            if (!string.Equals(readBack.ObjectType, AutomaticDebitWebhookObjectTypes.Payment, StringComparison.Ordinal) ||
                !string.Equals(readBack.Id, notification.Id, StringComparison.Ordinal) ||
                !string.Equals(readBack.CollectorId, configuration.CollectorId, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "El webhook de pago de PayPerTIC fue rechazado por discrepancia con el estado autoritativo. PagId={PagId} JurisdictionId={JurisdictionId} ProviderId={ProviderId}",
                    pago.PagId,
                    pago.PagIdJurisdiccion,
                    notification.Id);
                inbox.Result = PayPerTicPaymentWebhookInboxResults.Rejected;
                inbox.FailureCode = "authoritative_mismatch";
                inbox.ProcessedAt = _timeProvider.GetUtcNow().UtcDateTime;
                await _gtwContext.SaveChangesAsync();
                return OperationResponse<bool>.CustomErrorResponse(401, "La notificación no pudo ser validada.");
            }

            var sanitizedStatus = readBack.Status.Replace("_", "");

            if (!Enum.TryParse<PayPerTicPaymentStatus>(sanitizedStatus, true, out var statusEnum))
            {
                inbox.Result = PayPerTicPaymentWebhookInboxResults.Rejected;
                inbox.FailureCode = "unsupported_authoritative_state";
                inbox.ProcessedAt = _timeProvider.GetUtcNow().UtcDateTime;
                await _gtwContext.SaveChangesAsync();
                return BadRequest<bool>("PayPerTIC informó un estado no soportado.");
            }

            var nextState = MapearEstado(statusEnum);
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
            if (_gtwContext.Database.IsRelational())
            {
                transaction = await _gtwContext.Database.BeginTransactionAsync();
            }

            try
            {
                if (CanApplyPaymentTransition(pago.PagEstado, nextState))
                {
                    pago.PagEstado = nextState;
                    pago.PagEstadoDetalle = $"Estado confirmado por consulta autoritativa: {readBack.Status}";
                    _gtwContext.GtwPagos.Update(pago);
                }

                inbox.Result = PayPerTicPaymentWebhookInboxResults.Processed;
                inbox.FailureCode = null;
                inbox.ProcessedAt = _timeProvider.GetUtcNow().UtcDateTime;
                await _gtwContext.SaveChangesAsync();
                if (transaction is not null)
                {
                    await transaction.CommitAsync();
                }
            }
            catch
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync();
                }

                throw;
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }
            }

            return OperationResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            if (inbox is not null)
            {
                try
                {
                    inbox.Result = PayPerTicPaymentWebhookInboxResults.Failed;
                    inbox.FailureCode = "processing_error";
                    inbox.ProcessedAt = null;
                    await _gtwContext.SaveChangesAsync();
                }
                catch (Exception persistenceException)
                {
                    _logger.LogError(
                        persistenceException,
                        "No se pudo persistir el fallo del webhook de pago de PayPerTIC. InboxId={InboxId}",
                        inbox.Id);
                }
            }

            _logger.LogError(ex, "Ocurrió un error inesperado al procesar el webhook de pago de PayPerTIC.");
            return InternalServerError<bool>("No fue posible procesar la notificación del pago.");
        }
    }

    public async Task<OperationResponse<bool>> CancelarPagoAsync(string pagIdExterno, string motivo)
    {
        try
        {
            var pago = await _gtwContext.GtwPagos
                .FirstOrDefaultAsync(p => p.PagIdExterno == pagIdExterno);

            if (pago is null)
                return NotFound<bool>();

            if (pago.PagEstado != "pending")
                return BadRequest<bool>(
                    $"Solo se pueden cancelar pagos en estado 'pending'. Estado actual: {pago.PagEstado}");

            await _payPerTicClient.CancelPaymentAsync(pagIdExterno, new PayPerTicCancelRequest
            {
                StatusDetail = motivo
            });

            pago.PagEstado = "cancelled";
            pago.PagEstadoDetalle = motivo;
            _gtwContext.GtwPagos.Update(pago);
            await _gtwContext.SaveChangesAsync();

            return OperationResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error inesperado al cancelar el pago PayPerTIC {PaymentId}.", pagIdExterno);
            return InternalServerError<bool>("No fue posible cancelar el pago.");
        }
    }

    public async Task<OperationResponse<PayPerTicRefundResponse>> DevolverPagoAsync(
        string pagIdExterno, PayPerTicRefundRequest request)
    {
        try
        {
            var pago = await _gtwContext.GtwPagos
                .FirstOrDefaultAsync(p => p.PagIdExterno == pagIdExterno);

            if (pago is null)
                return NotFound<PayPerTicRefundResponse>();

            if (pago.PagEstado != "approved")
                return BadRequest<PayPerTicRefundResponse>(
                    $"Solo se pueden devolver pagos aprobados. Estado actual: {pago.PagEstado}");

            var refundResponse = await _payPerTicClient.RefundPaymentAsync(pagIdExterno, request);

            if (refundResponse.Status == "approved")
            {
                pago.PagEstado = "refunded";
                pago.PagEstadoDetalle = refundResponse.Reason ?? "Devolución aprobada";
                _gtwContext.GtwPagos.Update(pago);
                await _gtwContext.SaveChangesAsync();
            }

            return OperationResponse<PayPerTicRefundResponse>.SuccessResponse(refundResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error inesperado al devolver el pago PayPerTIC {PaymentId}.", pagIdExterno);
            return InternalServerError<PayPerTicRefundResponse>("No fue posible devolver el pago.");
        }
    }

    private PayPerTicCreatePaymentRequest BuildCreatePaymentRequest(
    CreatePayPerTicCheckoutDto request, string externalTransactionId)
    {
        var baseDate = _timeProvider.GetLocalNow();
        var defaultDueDate = new DateTimeOffset(baseDate.Year, baseDate.Month, baseDate.Day, 23, 59, 59, baseDate.Offset).AddDays(7);
        var defaultLastDueDate = defaultDueDate.AddDays(7);

        var metadataObject = new
        {
            Origen = request.Origen,
            Obligaciones = request.Details.Select(d => new
            {
                IdObligacion = d.IdObligacion ?? string.Empty,
                IdTributoContribuyente = d.IdTributoContribuyente ?? string.Empty,
                ClaveBien = d.ClaveBien ?? string.Empty,
                Concepto = d.ConceptDescription,
                NroCuota = d.NroCuota,
                AnoCuota = d.AnoCuota
            }).ToList()
        };

        return new PayPerTicCreatePaymentRequest
        {
            ExternalTransactionId = externalTransactionId,
            CurrencyId = request.CurrencyId,
            CollectorId = !string.IsNullOrWhiteSpace(_keys.CollectorId) ? _keys.CollectorId : null,

            Type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type,

            DueDate = !string.IsNullOrWhiteSpace(request.DueDate)
                        ? request.DueDate
                        : defaultDueDate.ToString("yyyy-MM-dd'T'HH:mm:sszzz").Remove(22, 1),

            LastDueDate = defaultLastDueDate.ToString("yyyy-MM-dd'T'HH:mm:sszzz").Remove(22, 1),

            NotificationUrl = _keys.NotificationUrl,
            ReturnUrl = _keys.ReturnUrl,
            BackUrl = _keys.BackUrl,
            Metadata = metadataObject,
            Payer = new PayPerTicPayer
            {
                Name = request.Payer.Name,
                Email = request.Payer.Email,
                ExternalReference = request.Payer.ExternalReference,
                Identification = new PayPerTicIdentification
                {
                    Type = request.Payer.IdentificationType,
                    Number = request.Payer.IdentificationNumber,
                    Country = request.Payer.IdentificationCountry
                }
            },
            Details = request.Details.Select(d => new PayPerTicPaymentDetail
            {
                Amount = d.Amount,
                ConceptId = d.ConceptId,
                ConceptDescription = d.ConceptDescription,
                ExternalReference = $"{d.IdTributoContribuyente}|{d.IdObligacion}|{d.ClaveBien}"
            }).ToList()
        };
    }

    /// <summary>
    /// Mapea el enum de PayPerTIC al estado interno almacenado en GtwPago.PagEstado.
    /// </summary>
    private static string MapearEstado(PayPerTicPaymentStatus status) => status switch
    {
        PayPerTicPaymentStatus.Pending => "pending",
        PayPerTicPaymentStatus.Issued => "pending",
        PayPerTicPaymentStatus.InProcess => "in_process",
        PayPerTicPaymentStatus.Approved => "approved",
        PayPerTicPaymentStatus.Rejected => "rejected",
        PayPerTicPaymentStatus.Cancelled => "cancelled",
        PayPerTicPaymentStatus.Refunded => "refunded",
        PayPerTicPaymentStatus.ChargedBack => "charged_back",
        PayPerTicPaymentStatus.InMediation => "in_mediation",
        _ => "error"
    };

    private static bool CanApplyPaymentTransition(string? currentState, string nextState)
    {
        var current = currentState?.Trim().ToLowerInvariant() ?? string.Empty;
        var next = nextState.Trim().ToLowerInvariant();
        if (current == next)
        {
            return true;
        }

        return current switch
        {
            "" or "error" or "pending" => true,
            "in_process" => next is "approved" or "rejected" or "cancelled" or "in_mediation",
            "approved" => next is "refunded" or "charged_back" or "in_mediation",
            "in_mediation" => next is "approved" or "refunded" or "charged_back",
            _ => false
        };
    }

    private static string CreateWebhookDeduplicationKey(
        long jurisdictionId,
        string providerPaymentId,
        string payloadHash)
    {
        var canonical = $"{jurisdictionId}:{providerPaymentId.Length}:{providerPaymentId}:{payloadHash}";
        return Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Obtiene el próximo valor de la secuencia de Oracle para PagId o PgdId.
    /// </summary>
    /// <param name="nombreSecuencia">Nombre de la secuencia en la base de datos.</param>
    /// <returns>El próximo valor de la secuencia.</returns>
    private async Task<int> ObtenerSecuenciaAsync(string nombreSecuencia)
    {
        if (!_gtwContext.Database.IsRelational())
        {
            return new Random().Next(1, 1000000);
        }

        await _gtwContext.Database.OpenConnectionAsync();
        using var command = _gtwContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT {nombreSecuencia}.NEXTVAL FROM DUAL";
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Llama al stored procedure para generar el comprobante en la BD.
    /// Esto es vital para que el Trigger de la bd luego pueda imputar el pago.
    /// </summary>
    private async Task GeneraComprobanteAsync(int pagId)
    {
        if (!_gtwContext.Database.IsRelational()) return; // Ignorar en tests InMemory

        using var cmd = _gtwContext.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "GATEWAY.PKG_GATEWAY.p_genera_comprobante";
        cmd.CommandType = CommandType.StoredProcedure;

        var pMsg = cmd.CreateParameter();
        pMsg.ParameterName = "v_id_msg";
        pMsg.Direction = ParameterDirection.Output;
        pMsg.DbType = DbType.String;
        pMsg.Size = 20000;
        cmd.Parameters.Add(pMsg);

        var pPagId = cmd.CreateParameter();
        pPagId.ParameterName = "p_pag_id";
        pPagId.Value = pagId;
        cmd.Parameters.Add(pPagId);

        await cmd.ExecuteNonQueryAsync();
    }
}
