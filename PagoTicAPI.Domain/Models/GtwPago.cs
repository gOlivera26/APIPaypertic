namespace PagoTicAPI.Domain.Models;

public partial class GtwPago
{
    public int PagId { get; set; }
    public DateTime PagFecha { get; set; }
    public string PagOrigen { get; set; } = null!;
    public string PagModoPago { get; set; } = null!;
    public string PagFormaPago { get; set; } = null!;
    public string PagMetodoPago { get; set; } = null!;
    public string PagEstado { get; set; } = null!;
    public string? PagEstadoDetalle { get; set; }
    public string PagMoneda { get; set; } = null!;
    public string? PagIdExterno { get; set; }
    public decimal PagImporteAbonado { get; set; }
    public string? PagIdCajero { get; set; }
    public decimal PagImporteCancelado { get; set; }
    public int? PagOpeId { get; set; }
    public int? PagCajId { get; set; }
    public int PagIdJurisdiccion { get; set; }

    public virtual ICollection<GtwPagoDetalle> GtwPagoDetalles { get; set; } = new List<GtwPagoDetalle>();
}
