namespace PagoTicAPI.Domain.Models;

public partial class GtwPagoDetalle
{
    public int PgdId { get; set; }

    public string PgdIdTributoContribuyente { get; set; } = null!;

    public string PgdConcepto { get; set; } = null!;

    public string PgdClaveBien { get; set; } = null!;

    public string? PgdContribuyente { get; set; }

    public string PgdIdObligacion { get; set; } = null!;

    public int PgdAnoCuota { get; set; }

    public int PgdNroCuota { get; set; }

    public decimal PgdCapitalFacturado { get; set; }

    public decimal PgdIntereses { get; set; }

    public decimal PgdDeudaActualizada { get; set; }

    public int PgdPagId { get; set; }

    public string? PgdTipoCuota { get; set; }

    public int? PgdIdComprobante { get; set; }

    public string? PgdTipoTributo { get; set; }

    public virtual GtwPago PgdPag { get; set; } = null!;
}
