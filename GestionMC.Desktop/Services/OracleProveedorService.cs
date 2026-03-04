using Oracle.ManagedDataAccess.Client;
using GestionMC.Desktop.Models;
using System;
using System.Collections.Generic;

namespace GestionMC.Desktop.Services;

public class OracleProveedorService : IOracleProveedorService
{
    private readonly string _connectionString;

    public OracleProveedorService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public List<ProveedorOracle> GetProveedores(bool testOnly = false)
    {
        var list = new List<ProveedorOracle>();

        string query = @"
SELECT PV_RUCCI, PV_RAZONS
FROM IN_PROVE
ORDER BY PV_RAZONS ASC";

        using (OracleConnection connection = new OracleConnection(_connectionString))
        {
            try
            {
                connection.Open();

                if (testOnly)
                {
                    connection.Close();
                    return list;
                }

                using (OracleCommand command = new OracleCommand(query, connection))
                {
                    using (OracleDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new ProveedorOracle
                            {
                                RucCi = reader["PV_RUCCI"]?.ToString()?.Trim() ?? "",
                                RazonSocial = reader["PV_RAZONS"]?.ToString()?.Trim() ?? ""
                            });
                        }
                    }
                }

                connection.Close();
            }
            catch
            {
                throw;
            }
        }

        return list;
    }
}
