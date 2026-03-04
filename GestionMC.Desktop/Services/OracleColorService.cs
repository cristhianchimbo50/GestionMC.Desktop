using System;
using System.Collections.Generic;
using GestionMC.Desktop.Models;
using Oracle.ManagedDataAccess.Client;

namespace GestionMC.Desktop.Services;

public class OracleColorService : IOracleColorService
{
    private readonly string _connectionString;

    public OracleColorService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public List<ColorFacturaRow> BuscarColores(string? cedula, string? chasis, string? nombre, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        var list = new List<ColorFacturaRow>();

        const string query = @"SELECT
    C.CE_CODIGO,
    C.CE_RUCIC,
    C.CE_NOMBRE,
    V.VE_NUMERO,
    V.VE_FECHA,
    D.DV_CHASIS,
    I.IT_CODANT,
    I.IT_NOMBRE,
    (D.DV_TOTAL * 1.15) AS TOTAL_CON_IVA
FROM FA_CLIEN C,
     FA_VENTA V,
     FA_DETVE D,
     IN_ITEM  I
WHERE V.CE_CODIGO = C.CE_CODIGO
  AND D.VE_NUMERO = V.VE_NUMERO
  AND D.IT_CODIGO = I.IT_CODIGO
  AND D.DV_CHASIS IS NOT NULL
  AND (:cedula IS NULL OR C.CE_RUCIC LIKE '%' || :cedula || '%')
  AND (:chasis IS NULL OR D.DV_CHASIS LIKE '%' || :chasis || '%')
  AND (:nombre IS NULL OR UPPER(C.CE_NOMBRE) LIKE '%' || UPPER(:nombre) || '%')
  AND (:fechaDesde IS NULL OR V.VE_FECHA >= :fechaDesde)
  AND (:fechaHasta IS NULL OR V.VE_FECHA <= :fechaHasta)
ORDER BY V.VE_FECHA DESC, V.VE_NUMERO DESC";

        using var connection = new OracleConnection(_connectionString);
        connection.Open();

        using var command = new OracleCommand(query, connection)
        {
            BindByName = true
        };

        command.Parameters.Add(new OracleParameter("cedula", string.IsNullOrWhiteSpace(cedula) ? DBNull.Value : cedula.Trim()));
        command.Parameters.Add(new OracleParameter("chasis", string.IsNullOrWhiteSpace(chasis) ? DBNull.Value : chasis.Trim()));
        command.Parameters.Add(new OracleParameter("nombre", string.IsNullOrWhiteSpace(nombre) ? DBNull.Value : nombre.Trim()));
        command.Parameters.Add(new OracleParameter("fechaDesde", fechaDesde.HasValue ? fechaDesde.Value.Date : DBNull.Value));
        command.Parameters.Add(new OracleParameter("fechaHasta", fechaHasta.HasValue ? fechaHasta.Value.Date : DBNull.Value));

        using var reader = command.ExecuteReader();

        int fechaIndex = reader.GetOrdinal("VE_FECHA");
        int totalIndex = reader.GetOrdinal("TOTAL_CON_IVA");

        while (reader.Read())
        {
            var fecha = reader.IsDBNull(fechaIndex) ? DateTime.MinValue : reader.GetDateTime(fechaIndex);
            var total = reader.IsDBNull(totalIndex) ? 0 : Convert.ToDecimal(reader.GetValue(totalIndex));

            list.Add(new ColorFacturaRow
            {
                Cedula = reader["CE_RUCIC"]?.ToString()?.Trim() ?? string.Empty,
                Nombre = reader["CE_NOMBRE"]?.ToString()?.Trim() ?? string.Empty,
                NumeroVenta = reader["VE_NUMERO"]?.ToString()?.Trim() ?? string.Empty,
                Fecha = fecha,
                Color = reader["DV_CHASIS"]?.ToString()?.Trim() ?? string.Empty,
                CodigoItem = reader["IT_CODANT"]?.ToString()?.Trim() ?? string.Empty,
                NombreItem = reader["IT_NOMBRE"]?.ToString()?.Trim() ?? string.Empty,
                TotalConIva = total
            });
        }

        return list;
    }
}
