using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Words
{
    class WordPos
    {
        private string word;
        private int pos;

        public WordPos(string word, int pos)
        {
            this.word = word;
            this.pos = pos;
        }

        public int GetPos()
        {
            return pos;
        }

        public string GetWord()
        {
            return word;
        }

        public override string ToString()
        {
            return word + ":" + pos;
        }
    }
}
