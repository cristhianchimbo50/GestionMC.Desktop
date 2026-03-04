namespace GestionMC.Desktop.Models;

public class ColorFacturaRow
{
    public string Cedula { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string NumeroVenta { get; set; } = "";
    public DateTime Fecha { get; set; } = DateTime.MinValue;
    public string Color { get; set; } = "";
    public string CodigoItem { get; set; } = "";
    public string NombreItem { get; set; } = "";
    public decimal TotalConIva { get; set; } = 0;
}
