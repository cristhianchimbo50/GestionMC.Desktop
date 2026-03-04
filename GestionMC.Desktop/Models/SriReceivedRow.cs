namespace GestionMC.Desktop.Models;

public class SriReceivedRow
{
    public int Nro { get; set; }
    public int RowIndex { get; set; }

    public string RucEmisor { get; set; } = "";
    public string RazonSocialEmisor { get; set; } = "";

    public string ClaveAcceso { get; set; } = "";
    public string FechaEmision { get; set; } = "";

    public string NumeroFactura { get; set; } = "";
    public string XmlPath { get; set; } = "";

    public string CoNumero { get; set; } = "";
    public string CoFacpro { get; set; } = "";
    public string PmNrosec { get; set; } = "";
    public string PvRucciBd { get; set; } = "";
    public string PvRazonsBd { get; set; } = "";
    public string RfCodigo { get; set; } = "";
    public string RfCodigo2 { get; set; } = "";
    public bool CoincideConBd { get; set; }
    public bool ProveedorExiste { get; set; }

    public string CoNumeroDisplay => string.IsNullOrWhiteSpace(CoNumero) ? "SIN REGISTRO" : CoNumero;

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
                    return "NO VALIDADA";
            }

            return PmNrosec;
        }
    }
}
