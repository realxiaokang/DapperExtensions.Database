using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace Dapper
{
    public class ColumnInfo
    {
        public PropertyInfo Property { get; set; }
        public string ColumnName { get; set; }
        public bool IsKey { get; set; }
        public DatabaseGeneratedOption DatabaseGeneratedOption { get; set; }
    }
}
