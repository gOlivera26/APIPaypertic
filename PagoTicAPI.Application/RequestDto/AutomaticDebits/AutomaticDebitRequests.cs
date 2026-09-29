namespace PagoTicAPI.Application.RequestDto.AutomaticDebits;

public sealed record CreateAutomaticDebitAdhesionRequest(
    [Range(typeof(long), "1", "9223372036854775807")]
    long IdJurisdiccion,
    [Required, StringLength(50, MinimumLength = 1)]
    [RegularExpression("^[A-Za-z0-9._-]+$")]
    string IdTributoContribuyente);

public sealed record CancelAutomaticDebitAdhesionRequest(
    [Required, StringLength(500, MinimumLength = 1)]
    string Reason);

public sealed record CreateAutomaticDebitRequest(long IdJurisdiccion, string IdObligacion);

public sealed record CancelAutomaticDebitRequest(string Reason);
