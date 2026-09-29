using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using PagoTicAPI.Domain.Models;


namespace PagoTicAPI.Domain.Context;

public partial class gtwContext : DbContext
{
    public gtwContext(DbContextOptions<gtwContext> options)
        : base(options)
    {
    }

    public virtual DbSet<GtwPago> GtwPagos { get; set; }

    public virtual DbSet<GtwPagoDetalle> GtwPagoDetalles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDefaultSchema("GATEWAY")
            .UseCollation("USING_NLS_COMP");

        modelBuilder.Entity<GtwPago>(entity =>
        {
            entity.HasKey(e => e.PagId).HasName("PAG_PK");

            entity.ToTable("GTW_PAGO");

            entity.HasIndex(e => e.PagEstado, "IDX_PAG_ESTADO");

            entity.HasIndex(e => e.PagIdJurisdiccion, "IDX_PAG_ID_JURISDICCION");

            entity.Property(e => e.PagId)
               .ValueGeneratedOnAdd()
               .HasColumnType("NUMBER")
               .HasColumnName("PAG_ID");
            entity.Property(e => e.PagCajId)
                .HasComment("Número de Caja")
                .HasColumnType("NUMBER")
                .HasColumnName("PAG_CAJ_ID");
            entity.Property(e => e.PagEstado)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasComment("Pendiente, autorizado, Rechazado")
                .HasColumnName("PAG_ESTADO");
            entity.Property(e => e.PagEstadoDetalle)
                .IsUnicode(false)
                .HasComment("Detalle del estado (rechazado por falta de fondos, requiere autorización, etc)")
                .HasColumnName("PAG_ESTADO_DETALLE");
            entity.Property(e => e.PagFecha)
                .HasPrecision(6)
                .HasDefaultValueSql("SYSDATE ")
                .HasComment("Fecha registro pago")
                .HasColumnName("PAG_FECHA");
            entity.Property(e => e.PagFormaPago)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasComment("Indica la forma de pago utilizado (efectivo, tarjeta, etc)")
                .HasColumnName("PAG_FORMA_PAGO");
            entity.Property(e => e.PagIdCajero)
                .HasMaxLength(250)
                .IsUnicode(false)
                .HasComment("Usuario del cajero que registró el pago")
                .HasColumnName("PAG_ID_CAJERO");
            entity.Property(e => e.PagIdExterno)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasComment("Id de pago en Mercado Pago")
                .HasColumnName("PAG_ID_EXTERNO");
            entity.Property(e => e.PagIdJurisdiccion)
                .HasDefaultValueSql("1 ")
                .HasComment("Id de Jurisdicción")
                .HasColumnType("NUMBER")
                .HasColumnName("PAG_ID_JURISDICCION");
            entity.Property(e => e.PagImporteAbonado)
                .HasComment("Importe abonado por este medio (incluye intereses que puede cobrar la plataforma por ejemplo por el pago de cuotas con interés)")
                .HasColumnType("NUMBER")
                .HasColumnName("PAG_IMPORTE_ABONADO");
            entity.Property(e => e.PagImporteCancelado)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("0 ")
                .HasComment("Importe total cancelado por el detalle")
                .HasColumnType("NUMBER")
                .HasColumnName("PAG_IMPORTE_CANCELADO");
            entity.Property(e => e.PagMetodoPago)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasComment("visa, mastercard, pago fácil, etc")
                .HasColumnName("PAG_METODO_PAGO");
            entity.Property(e => e.PagModoPago)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasComment("modo de pago utilizado (cobro presencial, mercado pago)")
                .HasColumnName("PAG_MODO_PAGO");
            entity.Property(e => e.PagMoneda)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasComment("ARS / USD / etc")
                .HasColumnName("PAG_MONEDA");
            entity.Property(e => e.PagOpeId)
                .HasComment("Número de Operación")
                .HasColumnType("NUMBER")
                .HasColumnName("PAG_OPE_ID");
            entity.Property(e => e.PagOrigen)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasComment("Indica si es pago por caja, web, etc")
                .HasColumnName("PAG_ORIGEN");
        });

        modelBuilder.Entity<GtwPagoDetalle>(entity =>
        {
            entity.HasKey(e => e.PgdId).HasName("PGD_PK");

            entity.ToTable("GTW_PAGO_DETALLE");

            entity.HasIndex(e => e.PgdIdComprobante, "IDX_PGD_ID_COMPROBANTE");

            entity.HasIndex(e => e.PgdIdObligacion, "IDX_PGD_ID_OBLIGACION");

            entity.HasIndex(e => e.PgdIdTributoContribuyente, "IDX_PGD_ID_TRIB_CONTRIB");

            entity.Property(e => e.PgdId)
                .ValueGeneratedOnAdd()
                .HasColumnType("NUMBER")
                .HasColumnName("PGD_ID");
            entity.Property(e => e.PgdAnoCuota)
                .HasColumnType("NUMBER(10,0)")
                .HasColumnName("PGD_ANO_CUOTA");
            entity.Property(e => e.PgdCapitalFacturado)
                .HasColumnType("NUMBER")
                .HasColumnName("PGD_CAPITAL_FACTURADO");
            entity.Property(e => e.PgdClaveBien)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("PGD_CLAVE_BIEN");
            entity.Property(e => e.PgdConcepto)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("PGD_CONCEPTO");
            entity.Property(e => e.PgdContribuyente)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("PGD_CONTRIBUYENTE");
            entity.Property(e => e.PgdDeudaActualizada)
                .HasColumnType("NUMBER")
                .HasColumnName("PGD_DEUDA_ACTUALIZADA");
            entity.Property(e => e.PgdIdComprobante)
                .HasColumnType("NUMBER")
                .HasColumnName("PGD_ID_COMPROBANTE");
            entity.Property(e => e.PgdIdObligacion)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("PGD_ID_OBLIGACION");
            entity.Property(e => e.PgdIdTributoContribuyente)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("PGD_ID_TRIBUTO_CONTRIBUYENTE");
            entity.Property(e => e.PgdIntereses)
                .HasColumnType("NUMBER")
                .HasColumnName("PGD_INTERESES");
            entity.Property(e => e.PgdNroCuota)
                .HasColumnType("NUMBER(10,0)")
                .HasColumnName("PGD_NRO_CUOTA");
            entity.Property(e => e.PgdPagId)
                .HasColumnType("NUMBER")
                .HasColumnName("PGD_PAG_ID");
            entity.Property(e => e.PgdTipoCuota)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("PGD_TIPO_CUOTA");
            entity.Property(e => e.PgdTipoTributo)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("PGD_TIPO_TRIBUTO");

            entity.HasOne(d => d.PgdPag).WithMany(p => p.GtwPagoDetalles)
                .HasForeignKey(d => d.PgdPagId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("PGD_PAG_FK");
        });
        modelBuilder.HasSequence("SEQ_CAJA");
        modelBuilder.HasSequence("SEQ_CAJERO");
        modelBuilder.HasSequence("SEQ_DOMINIO");
        modelBuilder.HasSequence("SEQ_NRO_RECIBO");
        modelBuilder.HasSequence("SEQ_OPERACIONES");
        modelBuilder.HasSequence("SEQ_PAGO");
        modelBuilder.HasSequence("SEQ_PAGO_APROBACION");
        modelBuilder.HasSequence("SEQ_PAGO_CAJA_CHEQUE");
        modelBuilder.HasSequence("SEQ_PAGO_CAJA_RETENCIONES");
        modelBuilder.HasSequence("SEQ_PAGO_CAJA_TARJETA");
        modelBuilder.HasSequence("SEQ_PAGO_CAJA_TRANSFERENCIA");
        modelBuilder.HasSequence("SEQ_PAGO_DETALLE");
        modelBuilder.HasSequence("SEQ_PAGO_DETALLE_TEMP");
        modelBuilder.HasSequence("SEQ_REIMPRESIONES");
        modelBuilder.HasSequence("SEQ_SEDE");
        modelBuilder.HasSequence("SEQ_USUARIO_SEDE");

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
