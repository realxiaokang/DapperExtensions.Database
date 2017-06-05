using Dapper;
using System;
using System.Data.SqlClient;
using System.Linq;

namespace DapperExtensions.Tests
{
    class Program
    {
        static int BlogId, PostNum = 1;

        static void Main(string[] args)
        {
            try
            {
                InitData();

                TestTableMetadata();
                TestInsert();
                TestQuery();
                TestUpdate();
                TestDelete();
            }
            finally
            {
                CleanData();
            }

            Console.ReadKey();
        }

        private static void CleanData()
        {
            using (var cnn = new SqlConnection(TestDatabase.ConnectionString))
            {
                var cmd = cnn.CreateCommand();
                cmd.CommandText = @"
DROP TABLE [dbo].[Blog];
DROP TABLE [dbo].[posts];";
                cnn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static void InitData()
        {
            using (var cnn = new SqlConnection(TestDatabase.ConnectionString))
            {
                var cmd = cnn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE [dbo].[Blog] (
    [Id]   INT           IDENTITY (1, 1) NOT NULL,
    [Name] NVARCHAR (50) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[posts] (
    [blog_id]        INT           NOT NULL,
    [post_num]       NCHAR (10)    NOT NULL,
    [computed_value] AS            ([blog_id]*(1000)),
    [title]          NVARCHAR (50) NULL,
    PRIMARY KEY CLUSTERED ([post_num] ASC, [blog_id] ASC)
);

INSERT INTO [dbo].[Blog] ([Name]) VALUES (N'My Blog');
INSERT INTO [dbo].[Blog] ([Name]) VALUES (N'My Blog');
INSERT INTO [dbo].[Blog] ([Name]) VALUES (N'My Blog');
INSERT INTO [dbo].[Blog] ([Name]) VALUES (N'My Blog');
INSERT INTO [dbo].[Blog] ([Name]) VALUES (N'My Blog');
";
                cnn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public static TestDatabase GetDb()
        {
            return TestDatabase.Init(new SqlConnection(TestDatabase.ConnectionString));
        }

        private static void TestInsert()
        {
            try
            {
                Console.Write("Testing Insert...");
                using (var db = GetDb())
                {
                    Blog blog = new Blog { Name = "My Blog" };
                    BlogId = blog.Id = db.Blogs.Insert(blog).Value;
                    db.Posts.Insert(new Post { BlogId = BlogId, PostNum = 1 });

                    if (db.Blogs.Get(BlogId) == null)
                    {
                        Console.WriteLine("FAIL ---> Insert Blog Failed.");
                    }

                    if (db.Posts.Get(new { BlogId = BlogId, PostNum = 1 }) == null)
                    {
                        Console.WriteLine("FAIL ---> Insert Post Failed.");
                    }
                }
                Console.WriteLine("OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL ---> {ex.Message}");
            }
        }

        private static void TestUpdate()
        {
            try
            {
                Console.Write("Testing Update...");
                using (var db = GetDb())
                {
                    db.Blogs.Update(BlogId, new { Name = "New Blog Name" });
                    db.Posts.Update(new { BlogId = BlogId, PostNum = PostNum }, new { Title = "New Post Title" });

                    if (!db.Blogs.Get(BlogId).Name.Equals("New Blog Name"))
                    {
                        Console.WriteLine("FAIL --> Blog Update Failed.");
                        return;
                    }

                    if (!db.Posts.Get(new { BlogId = BlogId, PostNum = PostNum }).Title.Equals("New Post Title"))
                    {
                        Console.WriteLine("FAIL --> Post Update Failed.");
                        return;
                    }
                }

                Console.WriteLine("OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL ---> {ex.Message}");
            }
        }

        private static void TestDelete()
        {
            try
            {
                Console.Write("Testing Delete...");
                using (var db = GetDb())
                {
                    db.Blogs.Delete(BlogId);
                    if (db.Blogs.Get(BlogId) != null)
                    {
                        Console.WriteLine("FAIL --> Delete Blog Failed.");
                        return;
                    }

                    db.Posts.Delete(new { BlogId = BlogId, PostNum = PostNum });
                    if (db.Posts.Get(new { BlogId = BlogId, PostNum = PostNum }) != null)
                    {
                        Console.WriteLine("FAIL --> Delete Post Failed.");
                        return;
                    }
                }

                Console.WriteLine("OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL ---> {ex.Message}");
            }
        }

        private static void TestQuery()
        {
            using (var db = GetDb())
            {
                Console.Write("Testing Get...");
                if (db.Blogs.Get(BlogId).Name == "My Blog")
                {
                    Console.WriteLine("OK");
                }
                else
                {
                    Console.WriteLine("FAIL");
                }

                Console.Write("Testing All...");
                if (db.Posts.All("ComputedValue = @value", new { value = BlogId * 1000 }).Count() != 1)
                {
                    Console.WriteLine("FAIL");
                }

                var blogs = db.Blogs.All(ordering: $"{nameof(Blog.Id)} DESC");
                if (blogs.First().Id == BlogId && blogs.Last().Id != BlogId)
                {
                    Console.WriteLine("OK");
                }
            }
        }

        private static void TestTableMetadata()
        {
            Console.Write("Testing Table Metadata...");
            var blogTableMetadata = TableMetadata.CreateTableMetadata(typeof(Blog));
            var postTableMetadata = TableMetadata.CreateTableMetadata(typeof(Post));
            Console.WriteLine(
                    blogTableMetadata.TableName == "Blog" &&
                    blogTableMetadata.HasKey &&
                    !blogTableMetadata.HasCompositeKey &&
                    blogTableMetadata.HasIdentityKey &&
                    blogTableMetadata.KeyProperties.Length == 1 &&
                    blogTableMetadata.KeyProperties[0] == "Id" &&
                    blogTableMetadata.ColumnPropertyMap["Id"] == "Id" &&
                    blogTableMetadata.PropertyColumnMap["Name"] == "Name" &&
                    postTableMetadata.TableName == "posts" &&
                    postTableMetadata.HasKey &&
                    postTableMetadata.HasCompositeKey &&
                    !postTableMetadata.HasIdentityKey &&
                    postTableMetadata.KeyProperties.Length == 2 &&
                    Array.IndexOf(postTableMetadata.ComputedProperties, "ComputedValue") > -1 &&
                    Array.IndexOf(postTableMetadata.KeyProperties, "BlogId") > -1 &&
                    Array.IndexOf(postTableMetadata.KeyProperties, "PostNum") > -1 &&
                    postTableMetadata.PropertyColumnMap["PostNum"] == "post_num" &&
                    postTableMetadata.PropertyColumnMap["BlogId"] == "blog_id" &&
                    postTableMetadata.PropertyColumnMap["ComputedValue"] == "computed_value" &&
                    postTableMetadata.ColumnPropertyMap["blog_id"] == "BlogId" &&
                    postTableMetadata.ColumnPropertyMap["post_num"] == "PostNum" &&
                    postTableMetadata.ColumnPropertyMap["computed_value"] == "ComputedValue"
                    ? "OK"
                    : "FAIL"
                );
        }
    }
}