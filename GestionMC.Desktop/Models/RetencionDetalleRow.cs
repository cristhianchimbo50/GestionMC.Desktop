using System;
using System.Linq;

namespace GestionMC.Desktop.Models;

public class RetencionDetalleRow
{
    public string CoNumero { get; set; } = "";
    public string PmNrosec { get; set; } = "";
    public string ItCodAnt { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Base15 { get; set; }
    public decimal Total { get; set; }

    public string CodigoProducto { get; set; } = "";
    public string Producto { get; set; } = "";
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal SubtotalLinea { get; set; }

    public string PmNrosecDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PmNrosec))
                return "SIN REGISTRO";

            if (string.Equals(PmNrosec, "GRAN CONTRIBUYENTE", StringComparison.OrdinalIgnoreCase))
                return PmNrosec;

            var cleaned = PmNrosec.Trim();
            if (cleaned.All(char.IsDigit))
            {
                var padded = cleaned.PadLeft(9, '0');
                if (string.CompareOrdinal(padded, "000001600") < 0)
                    return "GRAN CONTRIBUYENTE";
            }

            return PmNrosec;
        }
    }
}
