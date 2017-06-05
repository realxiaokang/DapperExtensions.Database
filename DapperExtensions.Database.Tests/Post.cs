using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DapperExtensions.Tests
{
    [Table("posts")]
    public class Post
    {
        [Key]
        [Column("blog_id")]
        public int BlogId { get; set; }

        [Key]
        [Column("post_num")]
        public int PostNum { get; set; }

        [Column("title")]
        public string Title { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [Column("computed_value")]
        public int ComputedValue { get; set; }
    }
}
