using Dapper;

namespace DapperExtensions.Tests
{
    public class TestDatabase : Database<TestDatabase>
    {
        public static readonly string ConnectionString = @"Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=tempdb;Integrated Security=True;Connect Timeout=15;Encrypt=False;TrustServerCertificate=True;ApplicationIntent=ReadWrite;MultiSubnetFailover=False";

        public Table<Blog> Blogs { get; set; }
        public Table<Post> Posts { get; set; }
    }
}
