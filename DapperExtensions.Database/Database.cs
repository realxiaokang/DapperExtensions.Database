using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;

namespace Dapper
{
    public class Database<TDatabase> : IDisposable
        where TDatabase : Database<TDatabase>, new()
    {
        DbConnection _connection;
        DbTransaction _transaction;
        int? _commandTimeout;

        #region Database API
        public void BeginTransaction(IsolationLevel level = IsolationLevel.ReadCommitted)
        {
            _transaction = _connection.BeginTransaction();
        }
        public void CommitTransaction()
        {
            _transaction.Commit();
            _transaction = null;
        }
        public void RollbackTransaction()
        {
            _transaction.Rollback();
            _transaction = null;
        }
        public int Execute(string sql, object param = null)
        {
            return _connection.Execute(sql, param, _transaction, _commandTimeout);
        }
        public IEnumerable<T> Query<T>(string sql, object param = null, bool buffered = true)
        {
            return _connection.Query<T>(sql,
                                        param,
                                        buffered: buffered,
                                        commandTimeout: _commandTimeout);
        }
        public IEnumerable<T> ProcQuery<T>(string procName, object param = null, bool buffered = true)
        {
            return _connection.Query<T>(procName,
                                        param,
                                        buffered: buffered,
                                        commandTimeout: _commandTimeout,
                                        commandType: CommandType.StoredProcedure);
        }
        #endregion

        public class Table<T>
        where T : class, new()
        {
            private Database<TDatabase> _database;
            private TableMetadata _metadata;

            public Table(Database<TDatabase> database)
            {
                _database = database;
                Init();
            }

            private void Init()
            {
                _metadata = GetTableMetadata();
            }

            private static readonly ConcurrentDictionary<Type, TableMetadata> TableMetadataCache = new ConcurrentDictionary<Type, TableMetadata>();
            private static TableMetadata GetTableMetadata()
            {
                var entityType = typeof(T);
                if (!TableMetadataCache.TryGetValue(entityType, out var metadata))
                {
                    metadata = TableMetadata.CreateTableMetadata(entityType);
                    TableMetadataCache[entityType] = metadata;
                }

                return metadata;
            }

            static readonly ConcurrentDictionary<Type, string> TableNameCache = new ConcurrentDictionary<Type, string>();
            private static string GetTableName()
            {
                if (!TableNameCache.TryGetValue(typeof(T), out var tableName))
                {
                    var tableAttr = typeof(T).GetTypeInfo().GetCustomAttribute<TableAttribute>();
                    tableName = tableAttr == null || string.IsNullOrWhiteSpace(tableAttr.Name)
                                        ? typeof(T).Name
                                        : tableAttr.Name;

                    TableNameCache[typeof(T)] = tableName;
                }

                return tableName;
            }

            static readonly ConcurrentDictionary<Type, List<ColumnInfo>> ColumnsInfoCache = new ConcurrentDictionary<Type, List<ColumnInfo>>();
            private static List<ColumnInfo> GetColumnsInfo()
            {
                if (!ColumnsInfoCache.TryGetValue(typeof(T), out var columnsInfo))
                {
                    var columnProperties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null);




                    ColumnsInfoCache[typeof(T)] = columnsInfo;
                }

                return columnsInfo;
            }

            public int? Insert(T entity)
            {
                var paramList = GetParamNames(entity);
                if (_metadata.HasIdentityKey)
                {
                    paramList.Remove(_metadata.KeyProperties[0]);
                }

                foreach (var computedProperty in _metadata.ComputedProperties)
                {
                    paramList.Remove(computedProperty);
                }

                string columns = string.Join(", ", paramList.Select(paramName => _metadata.PropertyColumnMap[paramName]));
                string values = string.Join(", ", paramList.Select(paramName => $"@{paramName}"));
                string sql = $"insert into {_metadata.TableName} ({columns}) values ({values}) select cast(scope_identity() as int)";

                return _database.Query<int?>(sql, entity).Single();
            }
            public int Update(object id, object data)
            {
                if (!_metadata.HasKey)
                {
                    throw new InvalidOperationException($"No primary key specified for entity '{typeof(T).FullName}'.");
                }

                var paramList = GetParamNames(data);
                if (_metadata.HasKey)
                {
                    foreach (string key in _metadata.KeyProperties)
                    {
                        paramList.Remove(key);
                    }
                }

                foreach (var computedProperty in _metadata.ComputedProperties)
                {
                    paramList.Remove(computedProperty);
                }

                StringBuilder builder = new StringBuilder($"update {_metadata.TableName}").Append(" set ");
                builder.AppendLine(string.Join(", ", paramList.Select(paramName => $"{_metadata.PropertyColumnMap[paramName]} = @{paramName}")));
                builder.Append("where ").Append(string.Join(" AND ", _metadata.KeyProperties.Select(key => $"{_metadata.PropertyColumnMap[key]} = @{key}")));

                DynamicParameters param = new DynamicParameters(data);
                if (_metadata.HasCompositeKey)
                {
                    param.AddDynamicParams(id);
                }
                else
                {
                    param.Add(_metadata.KeyProperties[0], id);
                }

                return _database.Execute(builder.ToString(), param);
            }

            public int Delete(object id)
            {
                WhereById(id, out string clause, out object param);

                StringBuilder builder = new StringBuilder($"delete from {_metadata.TableName}");
                builder.Append(" where ").Append(clause);

                return _database.Execute(builder.ToString(), param);
            }
            public T Get(object id)
            {
                WhereById(id, out string clause, out object param);

                StringBuilder builder = new StringBuilder("select ");
                builder.Append(string.Join(", ", _metadata.ColumnPropertyMap.Select(item => $"{item.Key} AS {item.Value}")));
                builder.AppendLine($" from {_metadata.TableName}");
                builder.Append("where ").Append(clause);

                return _database.Query<T>(builder.ToString(), param).SingleOrDefault();
            }

            public IEnumerable<T> All(string where = null, object param = null, string ordering = null)
            {
                StringBuilder builder = new StringBuilder("select ");
                builder.Append(string.Join(", ", _metadata.ColumnPropertyMap.Select(item => $"{item.Key} AS {item.Value}")));
                builder.AppendLine($" from {_metadata.TableName}");
                if (!string.IsNullOrWhiteSpace(where))
                {
                    builder.Append("where ").AppendLine(ReplacePropertyNameWithColumnName(where));
                }
                if (!string.IsNullOrWhiteSpace(ordering))
                {
                    builder.Append("order by ").AppendLine(ReplacePropertyNameWithColumnName(ordering));
                }

                return _database.Query<T>(builder.ToString(), param);
            }

            private string ReplacePropertyNameWithColumnName(string clause)
            {
                string s = clause;

                foreach (var map in _metadata.PropertyColumnMap)
                {
                    Regex rx = new Regex($"(?<!@){map.Key}", RegexOptions.IgnoreCase); // replace property name which not follow with '@' to the corresponding column name
                    s = rx.Replace(s, map.Value);
                }

                return s;
            }

            private void WhereById(object id, out string clause, out object param)
            {
                DynamicParameters parameter;
                if (_metadata.HasCompositeKey)
                {
                    parameter = new DynamicParameters(id);
                }
                else
                {
                    parameter = new DynamicParameters();
                    parameter.Add(_metadata.KeyProperties[0], id);
                }

                clause = string.Join(" AND ", _metadata.KeyProperties.Select(key => $"{_metadata.PropertyColumnMap[key]} = @{key}"));
                param = parameter;
            }

            static ConcurrentDictionary<Type, List<string>> paramNameCache = new ConcurrentDictionary<Type, List<string>>();

            internal static List<string> GetParamNames(object o)
            {
                var parameters = o as DynamicParameters;
                if (parameters != null)
                {
                    return parameters.ParameterNames.ToList();
                }

                List<string> paramNames;
                if (!paramNameCache.TryGetValue(o.GetType(), out paramNames))
                {
                    paramNames = new List<string>();
                    foreach (var prop in o.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetGetMethod(false) != null))
                    {
                        if (prop.GetCustomAttribute<NotMappedAttribute>() == null)
                        {
                            paramNames.Add(prop.Name);
                        }
                    }
                    paramNameCache[o.GetType()] = paramNames;
                }
                return paramNames;
            }
        }

        public static TDatabase Init(DbConnection connection, int? commandTimeout = null)
        {
            TDatabase database = new TDatabase();
            database.InitDatabase(connection, commandTimeout);
            return database;
        }

        private static Action<TDatabase> tableConstructor;
        private void InitDatabase(DbConnection connection, int? commandTimeout = null)
        {
            _connection = connection;
            _commandTimeout = commandTimeout;
            if (tableConstructor == null)
            {
                tableConstructor = CreateTableConstructorForTable();
            }

            tableConstructor(this as TDatabase);
        }

        private Action<TDatabase> CreateTableConstructorForTable()
        {
            return CreateTableConstructor(typeof(Table<>));
        }

        private Action<TDatabase> CreateTableConstructor(Type tableType)
        {
            var dm = new DynamicMethod("ConstructInstances", null, new[] { typeof(TDatabase) }, true);
            var il = dm.GetILGenerator();

            var setters = GetType().GetProperties()
                .Where(p => p.PropertyType.GetTypeInfo().IsGenericType && tableType == p.PropertyType.GetTypeInfo().GetGenericTypeDefinition())
                .Select(p => Tuple.Create(
                        p.GetSetMethod(true),
                        p.PropertyType.GetConstructor(new[] { typeof(TDatabase) }),
                        p.Name,
                        p.DeclaringType
                 ));

            foreach (var setter in setters)
            {
                il.Emit(OpCodes.Ldarg_0);
                // [db]

                il.Emit(OpCodes.Newobj, setter.Item2);
                // [table]

                var table = il.DeclareLocal(setter.Item2.DeclaringType);
                il.Emit(OpCodes.Stloc, table);
                // []

                il.Emit(OpCodes.Ldarg_0);
                // [db]

                il.Emit(OpCodes.Castclass, setter.Item4);
                // [db cast to container]

                il.Emit(OpCodes.Ldloc, table);
                // [db cast to container, table]

                il.Emit(OpCodes.Callvirt, setter.Item1);
                // []
            }

            il.Emit(OpCodes.Ret);
            return (Action<TDatabase>)dm.CreateDelegate(typeof(Action<TDatabase>));
        }

        public void Dispose()
        {
            if (_connection.State != ConnectionState.Closed)
            {
                _transaction?.Rollback();

                _connection.Close();
                _connection = null;
            }
        }
    }
}
