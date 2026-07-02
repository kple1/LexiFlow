using System;
using System.Collections.Generic;
using System.Text;

namespace LexiFlow.Models
{
    public class Grammar
    {
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string Example { get; set; } = "";
        public string Explanation { get; set; } = "";
        public string Note { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
