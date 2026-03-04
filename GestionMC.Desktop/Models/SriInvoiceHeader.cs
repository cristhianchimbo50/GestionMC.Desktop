namespace GestionMC.Desktop.Models;

public class SriInvoiceHeader
{
    public string ClaveAcceso { get; set; } = "";
    public string NumeroAutorizacion { get; set; } = "";
    public DateTime FechaAutorizacion { get; set; }

    public string RucEmisor { get; set; } = "";
    public string RazonSocialEmisor { get; set; } = "";
    public string NombreComercialEmisor { get; set; } = "";

    public string NumeroFactura { get; set; } = "";
    public DateTime FechaEmision { get; set; }
    public decimal Total { get; set; }

    public decimal Subtotal15 { get; set; }
    public decimal SubtotalNoObjetoIva { get; set; }
    public decimal SubtotalExentoIva { get; set; }
    public decimal SubtotalSinImpuestos { get; set; }
    public decimal TotalDescuento { get; set; }
    public decimal Iva15 { get; set; }

    public string XmlPath { get; set; } = "";
}
