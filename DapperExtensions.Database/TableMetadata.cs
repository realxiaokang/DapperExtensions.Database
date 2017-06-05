using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;

namespace Dapper
{
    public class TableMetadata
    {
        public string TableName { get; set; }
        public IDictionary<string, string> PropertyColumnMap { get; set; }
        public IDictionary<string, string> ColumnPropertyMap { get; set; }
        public string[] KeyProperties { get; set; }
        public string[] ComputedProperties { get; set; }
        public bool HasKey { get; set; }
        public bool HasCompositeKey { get; set; }
        public bool HasIdentityKey { get; set; }

        public static TableMetadata CreateTableMetadata(Type entityType)
        {
            var tableAttr = entityType.GetTypeInfo().GetCustomAttribute<TableAttribute>();

            var columnProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null);

            var columnsInfo = new List<ColumnInfo>();
            foreach (var property in columnProperties)
            {
                var columnAttr = property.GetCustomAttribute<ColumnAttribute>();
                var keyAttr = property.GetCustomAttribute<KeyAttribute>();
                var databaseGeneratedAttr = property.GetCustomAttribute<DatabaseGeneratedAttribute>();

                columnsInfo.Add(new ColumnInfo
                {
                    Property = property,
                    ColumnName = columnAttr == null || string.IsNullOrWhiteSpace(columnAttr.Name)
                                    ? property.Name
                                    : columnAttr.Name,
                    DatabaseGeneratedOption = databaseGeneratedAttr == null ? DatabaseGeneratedOption.None
                                                                            : databaseGeneratedAttr.DatabaseGeneratedOption,
                    IsKey = keyAttr != null
                });
            }
            if (columnsInfo.Count(c => c.IsKey) == 0)
            {
                var candidateKey = columnsInfo.FirstOrDefault(c => c.Property.Name.ToLower().Equals("id"))
                                    ?? columnsInfo.FirstOrDefault(c => c.Property.Name.ToLower().Equals($"{entityType.Name.ToLower()}id"));

                if (candidateKey != null)
                {
                    candidateKey.IsKey = true;
                    if (candidateKey.Property.PropertyType == typeof(int)
                        && candidateKey.Property.GetCustomAttribute<DatabaseGeneratedAttribute>() == null)
                    {
                        candidateKey.DatabaseGeneratedOption = DatabaseGeneratedOption.Identity;
                    }
                }
            }

            return new TableMetadata
            {
                TableName = tableAttr == null || string.IsNullOrWhiteSpace(tableAttr.Name)
                                ? entityType.Name
                                : tableAttr.Name,
                HasKey = columnsInfo.Count(c => c.IsKey) > 0,
                HasCompositeKey = columnsInfo.Count(c => c.IsKey) > 1,
                HasIdentityKey = columnsInfo.Count(c => c.IsKey) == 1
                                    && columnsInfo.Count(c => c.IsKey && c.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity) == 1,
                KeyProperties = columnsInfo.Where(c => c.IsKey).Select(c => c.Property.Name).ToArray(),
                ComputedProperties = columnsInfo.Where(c => c.DatabaseGeneratedOption == DatabaseGeneratedOption.Computed).Select(c => c.Property.Name).ToArray(),
                PropertyColumnMap = columnsInfo.ToDictionary(c => c.Property.Name, c => c.ColumnName),
                ColumnPropertyMap = columnsInfo.ToDictionary(c => c.ColumnName, c => c.Property.Name)
            };
        }
    }
}
