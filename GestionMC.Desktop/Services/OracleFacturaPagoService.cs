using Oracle.ManagedDataAccess.Client;
using GestionMC.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GestionMC.Desktop.Services;

public class OracleFacturaPagoService : IOracleFacturaPagoService
{
    private readonly string _connectionString;

    public OracleFacturaPagoService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public List<FacturaPagoRow> GetFacturasPago()
    {
        var list = new List<FacturaPagoRow>();

        const string query = @"SELECT M.CO_NUMERO,
       CASE
           WHEN PR.RF_CODIGO = 0
            AND PR.RF_CODIGO2 = 0
           THEN NULL
           ELSE MAX(P.PM_NROSEC)
       END AS PM_NROSEC,
       MAX(C.CO_FACPRO)  AS CO_FACPRO,
       MAX(PR.PV_RUCCI)  AS PV_RUCCI,
       MAX(PR.PV_RAZONS) AS PV_RAZONS,
       MAX(PR.RF_CODIGO)  AS RF_CODIGO,
       MAX(PR.RF_CODIGO2) AS RF_CODIGO2
FROM CP_MOVIM  M,
     CP_PAGO   P,
     IN_COMPRA C,
     IN_PROVE  PR
WHERE M.MP_CODIGO = P.MP_CODIGO
  AND M.CO_NUMERO = C.CO_NUMERO
  AND C.PV_CODIGO = PR.PV_CODIGO
  AND M.CO_NUMERO IS NOT NULL
GROUP BY M.CO_NUMERO, PR.RF_CODIGO, PR.RF_CODIGO2
ORDER BY M.CO_NUMERO DESC";

        using var connection = new OracleConnection(_connectionString);
        connection.Open();

        using var command = new OracleCommand(query, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var rf1 = reader["RF_CODIGO"]?.ToString()?.Trim() ?? "";
            var rf2 = reader["RF_CODIGO2"]?.ToString()?.Trim() ?? "";
            var pm = reader["PM_NROSEC"]?.ToString()?.Trim() ?? "";
            var coNumero = reader["CO_NUMERO"]?.ToString()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(coNumero))
                coNumero = "SIN REGISTRO";

            if ((rf1 == "0" || string.IsNullOrWhiteSpace(rf1)) && (rf2 == "0" || string.IsNullOrWhiteSpace(rf2)) && string.IsNullOrWhiteSpace(pm))
                pm = "GRAN CONTRIBUYENTE";

            list.Add(new FacturaPagoRow
            {
                CoNumero = coNumero,
                PmNrosec = pm,
                CoFacpro = reader["CO_FACPRO"]?.ToString()?.Trim() ?? "",
                PvRucci = reader["PV_RUCCI"]?.ToString()?.Trim() ?? "",
                PvRazons = reader["PV_RAZONS"]?.ToString()?.Trim() ?? "",
                RfCodigo = rf1,
                RfCodigo2 = rf2
            });
        }

        return list;
    }

    public bool ExisteCompra(string coNumero)
    {
        if (string.IsNullOrWhiteSpace(coNumero))
            return false;

        const string query = "SELECT 1 FROM CP_MOVIM WHERE CO_NUMERO = :coNumero AND ROWNUM = 1";

        using var connection = new OracleConnection(_connectionString);
        connection.Open();

        using var command = new OracleCommand(query, connection);
        command.Parameters.Add(new OracleParameter("coNumero", coNumero));

        using var reader = command.ExecuteReader();
        return reader.Read();
    }

    public List<RetencionDetalleRow> GetRetencionDetalle(string coNumero)
    {
        var list = new List<RetencionDetalleRow>();

        if (string.IsNullOrWhiteSpace(coNumero))
            return list;

        const string query = @"
            SELECT
                C.CO_NUMERO,
                X.PM_NROSEC,
                C.CO_SUBTOT,
                C.CO_IVA,
                C.CO_TARIFA12,
                C.CO_TOTAL,
                I.IT_CODANT,
                I.IT_NOMBRE,
                D.DC_CANTID,
                D.DC_COSTO,
                (D.DC_CANTID * D.DC_COSTO) AS LINEA_SUBTOTAL
            FROM IN_COMPRA C,
                 IN_DETCO  D,
                 IN_ITEM   I,
                 ( SELECT M.CO_NUMERO,
                          MAX(P.PM_NROSEC) AS PM_NROSEC
                     FROM CP_MOVIM M, CP_PAGO P
                    WHERE P.MP_CODIGO = M.MP_CODIGO
                    GROUP BY M.CO_NUMERO
                 ) X
            WHERE C.CO_NUMERO = D.CO_NUMERO
              AND D.IT_CODIGO = I.IT_CODIGO
              AND X.CO_NUMERO(+) = C.CO_NUMERO
              AND C.CO_NUMERO = :coNumero
            ORDER BY D.IT_CODIGO";

        using var connection = new OracleConnection(_connectionString);
        connection.Open();

        using var command = new OracleCommand(query, connection);
        command.Parameters.Add(new OracleParameter("coNumero", coNumero));

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var itCodAnt = reader["IT_CODANT"]?.ToString()?.Trim() ?? "";

            list.Add(new RetencionDetalleRow
            {
                CoNumero = reader["CO_NUMERO"]?.ToString()?.Trim() ?? "",
                PmNrosec = reader["PM_NROSEC"]?.ToString()?.Trim() ?? "",
                ItCodAnt = itCodAnt,
                Producto = reader["IT_NOMBRE"]?.ToString()?.Trim() ?? "",
                Cantidad = GetDecimal(reader, "DC_CANTID"),
                CostoUnitario = GetDecimal(reader, "DC_COSTO"),
                SubtotalLinea = GetDecimal(reader, "LINEA_SUBTOTAL"),
                Subtotal = GetDecimal(reader, "CO_SUBTOT"),
                Iva = GetDecimal(reader, "CO_IVA"),
                Base15 = GetDecimal(reader, "CO_TARIFA12"),
                Total = GetDecimal(reader, "CO_TOTAL")
            });
        }

        return list;
    }

    private static decimal GetDecimal(OracleDataReader reader, string column)
    {
        var value = reader[column];
        if (value == null || value == DBNull.Value)
            return 0;

        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }
}
