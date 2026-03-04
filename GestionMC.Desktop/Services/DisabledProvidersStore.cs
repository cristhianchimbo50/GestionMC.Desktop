using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace GestionMC.Desktop.Services;

public sealed class DisabledProvidersStore
{
    private readonly object _sync = new();
    private readonly string _filePath;

    public DisabledProvidersStore(string filePath)
    {
        _filePath = filePath;
        EnsureBaseFolder();
        EnsureFile();
    }

    public bool IsDisabled(string ruc)
    {
        ruc = NormalizeRuc(ruc);
        if (string.IsNullOrWhiteSpace(ruc)) return false;

        lock (_sync)
        {
            var doc = LoadDoc();
            var el = FindProvider(doc, ruc);
            if (el == null) return false;
            var disabled = (string?)el.Attribute("disabled") ?? "false";
            return disabled.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public List<DisabledProviderEntry> GetAll()
    {
        lock (_sync)
        {
            var doc = LoadDoc();
            var list = doc.Root?
                .Elements("provider")
                .Select(x => new DisabledProviderEntry
                {
                    Ruc = ((string?)x.Attribute("ruc") ?? "").Trim(),
                    RazonSocial = ((string?)x.Attribute("razonSocial") ?? "").Trim(),
                    Disabled = (((string?)x.Attribute("disabled") ?? "false").Trim())
                        .Equals("true", StringComparison.OrdinalIgnoreCase)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Ruc))
                .OrderBy(x => x.Ruc)
                .ToList();

            return list ?? new List<DisabledProviderEntry>();
        }
    }

    public void SetDisabled(string ruc, string razonSocial, bool disabled)
    {
        ruc = NormalizeRuc(ruc);
        if (string.IsNullOrWhiteSpace(ruc)) return;

        lock (_sync)
        {
            var doc = LoadDoc();
            var el = FindProvider(doc, ruc);

            if (el == null)
            {
                el = new XElement("provider");
                el.SetAttributeValue("ruc", ruc);
                el.SetAttributeValue("razonSocial", (razonSocial ?? "").Trim());
                el.SetAttributeValue("disabled", disabled ? "true" : "false");
                doc.Root!.Add(el);
            }
            else
            {
                el.SetAttributeValue("disabled", disabled ? "true" : "false");
                if (string.IsNullOrWhiteSpace((string?)el.Attribute("razonSocial")))
                    el.SetAttributeValue("razonSocial", (razonSocial ?? "").Trim());
            }

            SaveDoc(doc);
        }
    }

    public void Toggle(string ruc, string razonSocial)
    {
        ruc = NormalizeRuc(ruc);
        if (string.IsNullOrWhiteSpace(ruc)) return;

        lock (_sync)
        {
            var doc = LoadDoc();
            var el = FindProvider(doc, ruc);

            if (el == null)
            {
                el = new XElement("provider");
                el.SetAttributeValue("ruc", ruc);
                el.SetAttributeValue("razonSocial", (razonSocial ?? "").Trim());
                el.SetAttributeValue("disabled", "true");
                doc.Root!.Add(el);
            }
            else
            {
                var disabled = ((string?)el.Attribute("disabled") ?? "false")
                    .Equals("true", StringComparison.OrdinalIgnoreCase);

                el.SetAttributeValue("disabled", disabled ? "false" : "true");

                if (string.IsNullOrWhiteSpace((string?)el.Attribute("razonSocial")))
                    el.SetAttributeValue("razonSocial", (razonSocial ?? "").Trim());
            }

            SaveDoc(doc);
        }
    }

    private void EnsureBaseFolder()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    private void EnsureFile()
    {
        if (File.Exists(_filePath)) return;
        var doc = new XDocument(new XElement("disabledProviders"));
        SaveDoc(doc);
    }

    private XDocument LoadDoc()
    {
        try
        {
            return XDocument.Load(_filePath);
        }
        catch
        {
            var doc = new XDocument(new XElement("disabledProviders"));
            SaveDoc(doc);
            return doc;
        }
    }

    private void SaveDoc(XDocument doc)
    {
        var tmp = _filePath + ".tmp";
        doc.Save(tmp);
        File.Copy(tmp, _filePath, true);
        File.Delete(tmp);
    }

    private static XElement? FindProvider(XDocument doc, string ruc)
    {
        return doc.Root?
            .Elements("provider")
            .FirstOrDefault(x =>
                ((string?)x.Attribute("ruc") ?? "").Trim().Equals(ruc, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRuc(string ruc)
    {
        return (ruc ?? "").Trim();
    }
}

public sealed class DisabledProviderEntry
{
    public string Ruc { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public bool Disabled { get; set; }
}