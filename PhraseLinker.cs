using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Words
{
    class PhraseLinker
    {
        public string fraseNormal;
        public string fraseCanonica;
        public string fraseGramatical;
        public int location;
        public bool wasProcessed;
        public string targetWord;

        public PhraseLinker(string fraseNormal, int location, string targetWord)
        {
            this.fraseNormal = fraseNormal;
            this.location = location;
            this.wasProcessed = false;
            this.targetWord = targetWord;
        }
    }
}
