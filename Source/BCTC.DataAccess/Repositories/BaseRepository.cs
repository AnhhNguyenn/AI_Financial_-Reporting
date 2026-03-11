using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Repositories
{
    public abstract class BaseRepository
    {
        // Abstract property - class con phải implement
        protected abstract string ConnectionString { get; }

        // Virtual property - class con có thể override
        protected virtual int DefaultCommandTimeout => 30;

        /// <summary>
        /// Execute stored procedure và trả về danh sách entities
        /// </summary>
        protected async Task<IEnumerable<T>> ExecuteStoredProcAsync<T>(
            string storedProcName,
            object parameters = null,
            CommandType commandType = CommandType.StoredProcedure,
            int? commandTimeout = null) where T : class
        {
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                var result = await connection.QueryAsync<T>(
                    sql: storedProcName,
                    param: parameters,
                    commandType: commandType,
                    commandTimeout: commandTimeout ?? DefaultCommandTimeout
                );

                return result;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Error executing stored procedure '{storedProcName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Execute stored procedure và trả về single entity
        /// </summary>
        protected async Task<T> ExecuteStoredProcSingleAsync<T>(
            string storedProcName,
            object parameters = null,
            CommandType commandType = CommandType.StoredProcedure,
            int? commandTimeout = null) where T : class
        {
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                var result = await connection.QuerySingleAsync<T>(
                    sql: storedProcName,
                    param: parameters,
                    commandType: commandType,
                    commandTimeout: commandTimeout ?? DefaultCommandTimeout
                );

                return result;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Error executing stored procedure '{storedProcName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Execute stored procedure và trả về first hoặc default
        /// </summary>
        protected async Task<T> ExecuteStoredProcFirstOrDefaultAsync<T>(
            string storedProcName,
            object parameters = null,
            CommandType commandType = CommandType.StoredProcedure,
            int? commandTimeout = null) where T : class
        {
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                var result = await connection.QueryFirstOrDefaultAsync<T>(
                    sql: storedProcName,
                    param: parameters,
                    commandType: commandType,
                    commandTimeout: commandTimeout ?? DefaultCommandTimeout
                );

                return result;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Error executing stored procedure '{storedProcName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Execute stored procedure với output parameters
        /// </summary>
        protected async Task<(IEnumerable<T> Data, DynamicParameters OutputParams)> ExecuteStoredProcWithOutputAsync<T>(
            string storedProcName,
            DynamicParameters parameters,
            CommandType commandType = CommandType.StoredProcedure,
            int? commandTimeout = null) where T : class
        {
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                var result = await connection.QueryAsync<T>(
                    sql: storedProcName,
                    param: parameters,
                    commandType: commandType,
                    commandTimeout: commandTimeout ?? DefaultCommandTimeout
                );

                return (result, parameters);
            }
            catch (SqlException ex)
            {
                throw new Exception($"Error executing stored procedure '{storedProcName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Execute stored procedure không trả về data
        /// </summary>
        protected async Task<int> ExecuteStoredProcNonQueryAsync(
            string storedProcName,
            object parameters = null,
            CommandType commandType = CommandType.StoredProcedure,
            int? commandTimeout = null)
        {
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                var rowsAffected = await connection.ExecuteAsync(
                    sql: storedProcName,
                    param: parameters,
                    commandType: commandType,
                    commandTimeout: commandTimeout ?? DefaultCommandTimeout
                );

                return rowsAffected;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Error executing stored procedure '{storedProcName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Execute stored procedure trả về scalar value
        /// </summary>
        protected async Task<T> ExecuteStoredProcScalarAsync<T>(
            string storedProcName,
            object parameters = null,
            CommandType commandType = CommandType.StoredProcedure,
            int? commandTimeout = null)
        {
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                var result = await connection.ExecuteScalarAsync<T>(
                    sql: storedProcName,
                    param: parameters,
                    commandType: commandType,
                    commandTimeout: commandTimeout ?? DefaultCommandTimeout
                );

                return result;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Error executing stored procedure '{storedProcName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Execute multiple result sets
        /// </summary>
        protected async Task<SqlMapper.GridReader> ExecuteStoredProcMultipleAsync(
            string storedProcName,
            object parameters = null,
            CommandType commandType = CommandType.StoredProcedure,
            int? commandTimeout = null)
        {
            try
            {
                var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                var gridReader = await connection.QueryMultipleAsync(
                    sql: storedProcName,
                    param: parameters,
                    commandType: commandType,
                    commandTimeout: commandTimeout ?? DefaultCommandTimeout
                );

                return gridReader;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Error executing stored procedure '{storedProcName}': {ex.Message}", ex);
            }
        }
    }
}
