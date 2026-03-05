using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Windows;

namespace GestionMC.Desktop;

public partial class RetencionClienteDetailWindow : Window
{
    private readonly string _xmlPath;

    public RetencionClienteDetailWindow(string xmlPath)
    {
        InitializeComponent();
        _xmlPath = xmlPath;
        Loaded += (_, _) => Cargar();
    }

    private void Cargar()
    {
        try
        {
            if (!File.Exists(_xmlPath))
            {
                LblStatus.Text = "No se encuentra el archivo.";
                return;
            }

            var parsed = ParseRetencion(_xmlPath);
            if (parsed == null)
            {
                LblStatus.Text = "No se pudo interpretar el XML.";
                return;
            }

            LblCliente.Text = parsed.ClienteNombre;
            LblIdentificacion.Text = parsed.ClienteIdentificacion;
            LblTipoIdent.Text = parsed.ClienteTipoIdent;
            LblFechaEmision.Text = parsed.FechaEmision;
            LblPeriodoFiscal.Text = parsed.PeriodoFiscal;

            LblNumero.Text = parsed.NumeroComprobante;
            LblNumeroAutorizacion.Text = parsed.NumeroAutorizacion;
            LblFechaAut.Text = parsed.FechaAutorizacion;
            LblEstado.Text = parsed.Estado;

            GridDetalles.ItemsSource = parsed.Detalles;
            LblStatus.Text = "Listo.";
        }
        catch (Exception ex)
        {
            LblStatus.Text = "Error: " + ex.Message;
        }
    }

    private static ParsedRetencion? ParseRetencion(string path)
    {
        var doc = XDocument.Load(path);
        var autorizacion = doc.Descendants("autorizacion").FirstOrDefault();
        string? numeroAut = null;
        string? fechaAut = null;
        string? estado = null;
        XDocument? innerDoc = null;

        if (autorizacion != null)
        {
            numeroAut = autorizacion.Element("numeroAutorizacion")?.Value ?? "";
            fechaAut = autorizacion.Element("fechaAutorizacion")?.Value ?? "";
            estado = autorizacion.Element("estado")?.Value ?? "";

            var comprobanteCdata = autorizacion.Element("comprobante")?.Value;
            if (!string.IsNullOrWhiteSpace(comprobanteCdata))
            {
                innerDoc = XDocument.Parse(comprobanteCdata);
            }
        }

        innerDoc ??= doc;

        var root = innerDoc.Root;
        if (root == null)
            return null;

        var infoTrib = root.Descendants("infoTributaria").FirstOrDefault();
        var infoComp = root.Descendants("infoCompRetencion").FirstOrDefault();

        var estab = infoTrib?.Element("estab")?.Value ?? "";
        var ptoEmi = infoTrib?.Element("ptoEmi")?.Value ?? "";
        var sec = infoTrib?.Element("secuencial")?.Value ?? "";
        var numeroComp = FormatearNumero(estab, ptoEmi, sec);

        var fechaEmision = infoComp?.Element("fechaEmision")?.Value ?? "";
        var periodoFiscal = infoComp?.Element("periodoFiscal")?.Value ?? "";

        var clienteNombre = infoComp?.Element("razonSocialSujetoRetenido")?.Value
                            ?? infoTrib?.Element("razonSocial")?.Value
                            ?? infoTrib?.Element("nombreComercial")?.Value
                            ?? "";
        var clienteIdent = infoComp?.Element("identificacionSujetoRetenido")?.Value ?? "";
        var clienteTipoIdent = infoComp?.Element("tipoIdentificacionSujetoRetenido")?.Value ?? "";

        var detalles = new List<DetalleRetencion>();

        var docsSustento = root.Descendants("docSustento");
        foreach (var docSus in docsSustento)
        {
            var codDoc = docSus.Element("codDocSustento")?.Value ?? "";
            var comprobante = MapComprobante(codDoc);
            var numDoc = docSus.Element("numDocSustento")?.Value ?? "";
            var numDocFmt = FormatearNumDoc(numDoc);
            var fechaEmiDoc = docSus.Element("fechaEmisionDocSustento")?.Value ?? "";

            var retList = docSus.Descendants("retencion").ToList();
            if (retList.Count == 0)
            {
                detalles.Add(new DetalleRetencion
                {
                    Comprobante = comprobante,
                    Numero = numDocFmt,
                    FechaEmision = fechaEmiDoc,
                    EjercicioFiscal = periodoFiscal,
                    BaseImponible = "",
                    Impuesto = "",
                    Porcentaje = "",
                    ValorRetenido = ""
                });
                continue;
            }

            foreach (var ret in retList)
            {
                var baseImp = ret.Element("baseImponible")?.Value ?? "";
                var porcentaje = ret.Element("porcentajeRetener")?.Value ?? "";
                var valorRet = ret.Element("valorRetenido")?.Value ?? "";
                var codigo = ret.Element("codigo")?.Value ?? "";
                var impuesto = MapImpuesto(codigo);

                detalles.Add(new DetalleRetencion
                {
                    Comprobante = comprobante,
                    Numero = numDocFmt,
                    FechaEmision = fechaEmiDoc,
                    EjercicioFiscal = periodoFiscal,
                    BaseImponible = baseImp,
                    Impuesto = impuesto,
                    Porcentaje = porcentaje,
                    ValorRetenido = valorRet
                });
            }
        }

        return new ParsedRetencion
        {
            NumeroComprobante = numeroComp,
            NumeroAutorizacion = numeroAut ?? "",
            FechaAutorizacion = fechaAut ?? "",
            Estado = estado ?? "",
            ClienteNombre = clienteNombre,
            ClienteIdentificacion = clienteIdent,
            ClienteTipoIdent = clienteTipoIdent,
            FechaEmision = fechaEmision,
            PeriodoFiscal = periodoFiscal,
            Detalles = detalles
        };
    }

    private static string FormatearNumero(string estab, string ptoEmi, string sec)
    {
        if (string.IsNullOrWhiteSpace(estab) && string.IsNullOrWhiteSpace(ptoEmi) && string.IsNullOrWhiteSpace(sec))
            return "";
        return $"{estab}-{ptoEmi}-{sec}";
    }

    private static string FormatearNumDoc(string num)
    {
        var clean = num?.Trim() ?? "";
        if (clean.Length >= 9)
        {
            if (clean.Length >= 15)
                return $"{clean.Substring(0, 3)}-{clean.Substring(3, 3)}-{clean.Substring(6)}";
            if (clean.Length >= 12)
                return $"{clean.Substring(0, 3)}-{clean.Substring(3, 3)}-{clean.Substring(6)}";
        }
        return clean;
    }

    private static string MapComprobante(string cod)
    {
        return cod switch
        {
            "01" => "FACTURA",
            "03" => "LIQUIDACIÓN",
            "04" => "NOTA CRÉDITO",
            "05" => "NOTA DÉBITO",
            "06" => "GUÍA REMISIÓN",
            _ => cod
        };
    }

    private static string MapImpuesto(string cod)
    {
        return cod switch
        {
            "1" => "Impuesto a la Renta",
            "2" => "IVA",
            _ => string.IsNullOrWhiteSpace(cod) ? "" : cod
        };
    }

    private class ParsedRetencion
    {
        public string NumeroComprobante { get; set; } = "";
        public string NumeroAutorizacion { get; set; } = "";
        public string FechaAutorizacion { get; set; } = "";
        public string Estado { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
        public string ClienteIdentificacion { get; set; } = "";
        public string ClienteTipoIdent { get; set; } = "";
        public string FechaEmision { get; set; } = "";
        public string PeriodoFiscal { get; set; } = "";
        public List<DetalleRetencion> Detalles { get; set; } = new();
    }

    private class DetalleRetencion
    {
        public string Comprobante { get; set; } = "";
        public string Numero { get; set; } = "";
        public string FechaEmision { get; set; } = "";
        public string EjercicioFiscal { get; set; } = "";
        public string BaseImponible { get; set; } = "";
        public string Impuesto { get; set; } = "";
        public string Porcentaje { get; set; } = "";
        public string ValorRetenido { get; set; } = "";
    }
}
