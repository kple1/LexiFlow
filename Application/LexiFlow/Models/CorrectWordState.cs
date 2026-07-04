using System;
using System.Collections.Generic;
using System.Text;

namespace LexiFlow.Models
{
    public class CorrectWordState
    {
        public bool IsCorrect { get; set; }
        // True once the word has been answered wrong or revealed — used to decide
        // Mastered vs. Learning, and to record a single "wrong" result per word.
        public bool Missed { get; set; }
        public Word Word { get; set; }
    }
}
