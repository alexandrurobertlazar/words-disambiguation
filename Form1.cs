using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Words
{
    public partial class Form1 : Form
    {
        private ConcurrentDictionary<string, ConcurrentQueue<string>> wordsPhrases = new();
        private ConcurrentDictionary<nGram, int> nGramCounts = new();
        Regex wordRegex = new Regex(@"\p{L}*");
        public Form1()
        {
            InitializeComponent();
        }

        /***
         * 
         * Estructura de directorios:
         * En el directorio en el que se va a trabajar, deben existir cuatro cosas:
         * 3 directorios: "desambiguacion", "words" y "phrases"
         * --> "desambiguacion": Directorio donde se guardan los n-gramas.
         * --> "words": Directorio donde se almacenan las frases separadas por palabras.
         * --> "phrases": Directorio donde se encuentran las frases con las que se desea tratar. Debe haber mínimo un fichero de frases.
         * 1 fichero: "Palabras ambiguas.txt": Debe contener las palabras a tratar.
         * 
         * Al finalizarse el tratamiento de frases/palabras, se ejecuta un formulario, que
         * indicará el final de las operaciones de tratamiento.
         */
        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                // Load files
                // Aquí se debe especificar la ruta donde se encuentra el fichero con las palabras ambiguas.
                StreamReader wordsFile = new(@"C:\Users\Alexandru\Desktop\Beca Colaboración\FRASES\palabras ambiguas.txt", true);
                string[] words = wordsFile.ReadToEnd().Split("\r\n");
                wordsFile.Close();
                Parallel.ForEach(words, word =>
                {
                    wordsPhrases.TryAdd(word, new());
                });
                // Aquí se debe especificar la ruta del directorio que tiene los ficheros con las frases.
                foreach (string file in Directory.EnumerateFiles(@"C:\Users\Alexandru\Desktop\Beca Colaboración\FRASES\phrases", "*.txt"))
                {
                    StreamReader sr = new StreamReader(file);
                    string[] contents = sr.ReadToEnd().Split("\r\n");
                    Parallel.ForEach(contents, phrase =>
                    {
                        checkAmbiguousWordsInPhrase(phrase);
                    });
                    sr.Close();
                }
                Parallel.ForEach(wordsPhrases.Keys, word =>
                {
                    if(!wordsPhrases[word].IsEmpty) writeWordsPhrasesToFile(word);
                });
                Parallel.ForEach(wordsPhrases.Keys, word =>
                {
                    if (wordsPhrases[word].IsEmpty) return;
                    foreach (string phrase in wordsPhrases[word])
                    {
                        disambiguate(phrase, word);
                    }
                });
                foreach (nGram result in nGramCounts.Keys)
                {
                    try
                    {
                        // Aquí se especifica la carpeta donde se van a guardar los ficheros con los n-gramas. Solo se debe modificar el contenido del primer string!
                        StreamWriter sw = new StreamWriter(@"C:\Users\Alexandru\Desktop\Beca Colaboración\FRASES\desambiguacion\" + result.targetWord + "_" + result.orientation + "_" + result.nGramLength + ".txt", append: true);
                        sw.WriteLine(result.contents + ":" + result.distance + ":" + nGramCounts[result]);
                        sw.Flush();
                        sw.Close();
                    }
                    catch (Exception ex) { }
                }
                label1.Text = "Todas las palabras y frases se han procesado.";
            }
            catch (Exception ex)
            {
                label1.Text = ex.Message;
            }
        }

        private void disambiguate(string phrase, string targetWord, int occurrence = 1)
        {
            List<string> nGramsLeft = new();
            List<string> nGramsRight = new();
            string[] phraseParts = phrase.Split(" ");
            if (phraseParts.Length == 1)
            {
                string tempTargetWord = targetWord.Substring(0, 1).ToUpper() + targetWord.Substring(1);
                phraseParts = phrase.Split(tempTargetWord);
            }
            // Add words to nGrams.
            int qtyTargetWords = Regex.Matches(phrase, @"\b(?i)" + targetWord + @"\b").Count;
            if (qtyTargetWords > 1 && occurrence < qtyTargetWords)
            {
                disambiguate(phrase, targetWord, occurrence+1);
            }
            bool flagFound = false;
            foreach (string word in phraseParts)
            {
                if (wordRegex.Match(word.ToLower()).ToString() == targetWord.ToLower() && occurrence > 0)
                {
                    occurrence--;
                    if (occurrence == 0)
                    {
                        flagFound = true;
                        continue;
                    }                    
                }
                if (!flagFound)
                {
                    nGramsLeft.Add(word);
                } else
                {
                    nGramsRight.Add(word);
                }
            }
            int distance = 3;
            for (int i = 0; i < distance; i++)
            {
                string[] dividedPhrase = nGramsLeft.ToArray();
                int nGram = 1;
                while (true)
                {
                    nGram result = processNGram(getNGramAtDistance(i + 1, nGram, dividedPhrase, "left"), targetWord, "left", nGram, i + 1);
                    if (result.contents == "") break;
                    nGram++;
                }
                dividedPhrase = nGramsRight.ToArray();
                nGram = 1;
                while (true)
                {
                    nGram result = processNGram(getNGramAtDistance(i + 1, nGram, dividedPhrase, "right"), targetWord, "right", nGram, i + 1);
                    if (result.contents == "") break;
                    nGram++;
                }
            }
            return;
        }
        private string getNGramAtDistance(int distance, int nGram, string[] dividedPhrase, string orientation)
        {
            string result = "";
            int count = 0;
            if (orientation == "left")
            {
                for (int i = dividedPhrase.Length - nGram - distance + 1; i < dividedPhrase.Length - distance + 1; i++)
                {
                    if (i < 0 || count == nGram) break;
                    result += " " + dividedPhrase[i];
                    count++;
                }
            }
            else
            {
                bool flag = false;
                for (int i = distance - 1; i < dividedPhrase.Length; i++)
                {
                    if (i >= dividedPhrase.Length || count == nGram) break;
                    if (dividedPhrase.Length - i >= nGram || flag)
                    {
                        flag = true;
                    }
                    else
                    {
                        break;
                    }
                    if (flag)
                    {
                        result += " " + dividedPhrase[i];
                        count++;
                    }
                }
            }
            return result.Trim();
        }
        private nGram processNGram(string processedGram, string targetWord, string orientation, int nGram, int distance)
        {
            bool resultExists = false;
            nGram ngram = new nGram(distance, processedGram.Trim(), nGram, targetWord.ToLower(), orientation == "left" ? "izq" : "der");
            if (nGramCounts.ContainsKey(ngram))
            {
                nGramCounts[ngram]++;
            }
            if (!resultExists && ngram.contents != "")
            {
                nGramCounts.TryAdd(ngram, 1);
            }
            return ngram;
        }

        private int checkNgramDistance(string phrase, string processedGram, string targetWord)
        {
            if (!phrase.Contains(processedGram)) return -1;
            int distance = 1;
            string[] splitByNgram = phrase.Split(processedGram);
            if (splitByNgram[0].Contains(targetWord))
            {
                string[] separatedWords = splitByNgram[0].Trim().Split(" ");
                for (int i = separatedWords.Length - 1; i >= 0; i--)
                {
                    if (separatedWords[i] == targetWord) break;
                    distance++;
                }
            }
            else if (splitByNgram[1].Contains(targetWord))
            {
                string[] separatedWords = splitByNgram[1].Trim().Split(" ");
                for (int i = 0; i < separatedWords.Length; i++)
                {
                    if (separatedWords[i] == targetWord) break;
                    distance++;
                }
            }
            return distance;
        }
        private void checkAmbiguousWordsInPhrase(string phrase)
        {
            string[] phraseWords = phrase.Split(" ");
            foreach (string word in phraseWords)
            {
                string regexedWord = wordRegex.Match(word).ToString().ToLower();
                if (wordsPhrases.ContainsKey(regexedWord))
                {
                    wordsPhrases[regexedWord].Enqueue(phrase);
                }
            }
            return;
        }

        private void writeWordsPhrasesToFile(string word)
        {
            StreamWriter streamWriter = null;
            try
            {
                // Aquí se debe especificar la carpeta donde se deben guardar los ficheros con las frases de cada palabra. Solo se ha de modificar el primer string.
                streamWriter = new StreamWriter(@"C:\Users\Alexandru\Desktop\Beca Colaboración\FRASES\words\" + word + ".txt", append: true);
                foreach (string phrase in wordsPhrases[word].Distinct().ToList())
                {
                    streamWriter.WriteLine(phrase);
                }
                streamWriter.Flush();
                streamWriter.Close();
            }
            catch (Exception e)
            {
            }
            finally
            {
                if (streamWriter != null) streamWriter.Close();
            }
            return;
        }
    }
}
