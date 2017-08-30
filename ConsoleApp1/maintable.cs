namespace ConsoleApp1
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("reports.maintable")]
    public partial class maintable
    {
        public int id { get; set; }

        public DateTime timestamp { get; set; }

        [Column(TypeName = "tinytext")]
        [Required]
        [StringLength(255)]
        public string username { get; set; }

        [Column(TypeName = "tinytext")]
        [Required]
        [StringLength(255)]
        public string role { get; set; }

        [Column(TypeName = "tinytext")]
        [Required]
        [StringLength(255)]
        public string point { get; set; }

        [Required]
        [StringLength(16777215)]
        public string message { get; set; }

        [Required]
        [StringLength(45)]
        public string source { get; set; }
    }
}
