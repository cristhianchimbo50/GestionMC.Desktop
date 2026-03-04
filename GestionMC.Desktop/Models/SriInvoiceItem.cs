namespace GestionMC.Desktop.Models;

public class SriInvoiceItem
{
    public string CodigoPrincipal { get; set; } = "";
    public string CodigoAuxiliar { get; set; } = "";
    public string Descripcion { get; set; } = "";

    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }

    public decimal DescuentoUnitario { get; set; }

    public decimal BaseImponible { get; set; }
    public decimal Iva { get; set; }
    public decimal TotalLinea { get; set; }

    private decimal? _precioUnitarioConDescuentoOverride;

    public decimal PrecioUnitarioConDescuento
    {
        get
        {
            if (_precioUnitarioConDescuentoOverride.HasValue)
                return _precioUnitarioConDescuentoOverride.Value;

            var totalBruto = PrecioUnitario * (Cantidad == 0 ? 1 : Cantidad);
            var totalNeto = totalBruto - Descuento;
            return Cantidad == 0 ? totalNeto : totalNeto / Cantidad;
        }
        set => _precioUnitarioConDescuentoOverride = value;
    }
}
