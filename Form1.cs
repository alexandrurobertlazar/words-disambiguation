using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using ServicioLematizacion;

namespace Words
{
    public partial class Form1 : Form
    {

        // ConcurrentDictionary<nGram, int> nGramCounts = new();
        // Aquí se debe especificar la ruta del directorio base.
        private ConcurrentDictionary<string, ConcurrentQueue<string>> wordsPhrases = new();
        private ConcurrentDictionary<string, ConcurrentQueue<string>> wordsPhrasesCanonica = new();
        private ConcurrentDictionary<string, ConcurrentQueue<string>> wordsPhrasesCategorias = new();
        private ConcurrentBag<WordPos> wordsPositionsInPhrases = new();
        private string baseDirectory;
        Regex wordRegex = new Regex(@"\p{L}+");
        public Form1()
        {
            InitializeComponent();
        }

        /***
         * 
         * Estructura de directorios:
         * En el directorio en el que se va a trabajar, deben existir tres cosas:
         * 2 directorios: "desambiguacion" y "phrases"
         * --> "desambiguacion": Directorio donde se guardan los n-gramas y las frases.
         * --> "phrases": Directorio donde se encuentran las frases con las que se desea tratar. Debe haber mínimo un fichero de frases.
         * 1 fichero: "Palabras ambiguas.txt": Debe contener las palabras a tratar.
         */
        private void Form1_Load(object sender, EventArgs e)
        {}
        /***
         * Aquí es donde se empieza a trabajar.
         */
        async private Task work ()
        {
            try
            {
                ServicioLematizacionClient servicioLematizacion = new();
                progressBar1.Minimum = 0;
                progressBar1.Value = 0;
                wordsPhrases = new();
                label2.Text = "Cargando palabras...";
                label2.Refresh();
                StreamReader wordsFile = null;
                try
                {
                    wordsFile = new(baseDirectory + "palabras ambiguas.txt", true);
                } catch (Exception e)
                {
                    label2.Text = "Error: No se han podido cargar las palabas ambiguas. Verifique que el directorio es correcto.";
                    label2.Refresh();
                    return;
                }
                string[] words = wordsFile.ReadToEnd().Split("\r\n");
                wordsFile.Close();
                progressBar1.Maximum = words.Length;
                progressBar1.Refresh();
                await Task.Run(() => Parallel.ForEach(words, new ParallelOptions { MaxDegreeOfParallelism = 50 }, word =>
                {
                    wordsPhrases.TryAdd(word, new());
                    if (!Directory.Exists(baseDirectory + @"desambiguación\" + word))
                    {
                        Directory.CreateDirectory(baseDirectory + @"desambiguación\" + word);
                    }
                    
                    this.Invoke(new Action(() => progressBar1.Increment(1)));
                }));
                progressBar1.Value = 0;
                progressBar1.Maximum = Directory.EnumerateFiles(baseDirectory + "phrases", "*.txt").Count()*2;
                progressBar1.Refresh();
                label2.Text = "Cargando frases...";
                label2.Refresh();
                foreach (string file in Directory.EnumerateFiles(baseDirectory + "phrases", "*.txt"))
                {
                    StreamReader sr = new StreamReader(file);
                    string[] contents = sr.ReadToEnd().Split("\r\n");
                    this.Invoke(new Action(() => progressBar1.Increment(1)));
                    Parallel.ForEach(contents, new ParallelOptions { MaxDegreeOfParallelism = 50 }, phrase =>
                    {
                        CheckAmbiguousWordsInPhrase(phrase);
                    });
                    sr.Close();
                }
                // Las categorías las sacamos aquí para no hacer tantas llamadas a la API.
                Dictionary<int, InfoCategoria> categorias = servicioLematizacion.ConsultaCodigosCategorias();

                // Aquí es donde se lanza la obtención de frases por forma canónica y gramatical.
                await Task.Run(() => Parallel.ForEach(wordsPhrases.Keys, new ParallelOptions {MaxDegreeOfParallelism = 5}, word =>
                {
                    ObtenerFormaCanonicaGramatical(word, categorias);
                    this.Invoke(new Action(() => progressBar1.Increment(1)));
                }));
                label2.Text = "Escribiendo frases en ficheros...";
                label2.Refresh();
                progressBar1.Value = 0;
                progressBar1.Maximum = wordsPhrases.Count;
                progressBar1.Refresh();
                await Task.Run(() => Parallel.ForEach(wordsPhrases.Keys, word =>
                {
                    if (!wordsPhrases[word].IsEmpty) WriteWordsPhrasesToFile(word, "frases", wordsPhrases);
                    this.Invoke(new Action(() => progressBar1.Increment(1)));
                }));
                await Task.Run(() => Parallel.ForEach(wordsPhrasesCanonica.Keys, word =>
                {
                    if (!wordsPhrasesCanonica[word].IsEmpty) WriteWordsPhrasesToFile(word, "frases_lemas", wordsPhrasesCanonica);
                    this.Invoke(new Action(() => progressBar1.Increment(1)));
                }));
                await Task.Run(() => Parallel.ForEach(wordsPhrasesCategorias.Keys, word =>
                {
                    if (!wordsPhrasesCategorias[word].IsEmpty) WriteWordsPhrasesToFile(word, "frases_categorias_gramaticales", wordsPhrasesCategorias);
                    this.Invoke(new Action(() => progressBar1.Increment(1)));
                }));
                progressBar1.Value = 0;
                label2.Text = "Desambiguando...";
                label2.Refresh();
                progressBar1.Refresh();
                await Task.Run(() => Parallel.ForEach(wordsPhrases.Keys, new ParallelOptions { MaxDegreeOfParallelism = 4 }, word =>
                {
                    ConcurrentDictionary<nGram, int> nGramCounts = new();
                    ConcurrentDictionary<nGram, int> nGramCountsCanonicas = new();
                    ConcurrentDictionary<nGram, int> nGramCountsGramaticales = new();
                    if (wordsPhrases[word].IsEmpty) return;
                    Parallel.ForEach(wordsPhrases[word], new ParallelOptions { MaxDegreeOfParallelism = 4 }, (phrase, state, index) =>
                    {
                        Disambiguate(nGramCounts, phrase, word);
                        string[] phraseWords = phrase.Split(" ");
                        int targetPos = wordsPositionsInPhrases.ToArray()[index].GetPos();
                    });
                    foreach (nGram result in nGramCounts.Keys)
                    {
                        try
                        {
                            StreamWriter sw = new StreamWriter(baseDirectory + @"desambiguación\" + result.targetWord + @"\" + result.targetWord + "_" + result.orientation + "_" + result.nGramLength + ".txt", append: true);
                            sw.WriteLine(result.contents + ":" + result.distance + ":" + nGramCounts[result]);
                            sw.Flush();
                            sw.Close();
                        }
                        catch (Exception ex)
                        {
                            this.Invoke(new Action(() => label2.Text = "Ha habido un problema al procesar el n-grama: " + result));
                            return;
                        }
                    }
                    this.Invoke(new Action(() => { progressBar1.Increment(1); label3.Text = "Última palabra desambiguada: " + word; }));
                }));
                label2.Text = "Todas las palabras y frases se han procesado.";
            }
            catch (Exception ex)
            {
                label2.Text = ex.Message;
            }
        }
        // esto es simplemente un enumerable que permite ajustar el tamaño del incremento para ser usado en un Parallel.forEach
        static class BetterEnumerable
        {
            public static IEnumerable<int> SteppedRange(int fromInclusive, int toExclusive, int step)
            {
                for (var i = fromInclusive; i < toExclusive; i += step)
                {
                    yield return i;
                }
            }
        }

        private void ObtenerFormaCanonicaGramatical(string word, Dictionary<int, InfoCategoria> categorias)
        {
            // Ya que obtener muchas frases de una vez tarda mucho tiempo,
            // he optado por obtenerlas de 50 en 50, en 10 hilos diferentes
            // (tal como acordamos en la reunión del pasado miércoles)
            Parallel.ForEach(BetterEnumerable.SteppedRange(0, wordsPhrases[word].Count, 50), new ParallelOptions {MaxDegreeOfParallelism = 10}, (index) =>
            {
                ServicioLematizacionClient servicio = new();
                List<ParamtextFrase> infoFrases = new();
                List<ParamtextFrase> frasesResueltas = new();
                List<string> phraseCollection = wordsPhrases[word].Skip(index).Take(50).ToList();
                foreach (string phrase in phraseCollection)
                {
                    string[] phraseContents = phrase.Split(" ");
                    ParamtextFrase str = CreateParamtextFrase(phrase, phraseContents.Select(x => x.ToString().Trim()).Where(x => x != "").ToList());
                    infoFrases.Add(str);
                }
                frasesResueltas = servicio.ReconocerLinguakit(infoFrases,"es",false);
                ConcurrentQueue<string> frasesCanónicas = new();
                ConcurrentQueue<string> frasesCategorías = new();
                foreach (ParamtextFrase frase in frasesResueltas)
                {
                    string fraseCanonicas = "";
                    string fraseCategorias = "";
                    for (int i = 0; i < frase.Palabras.Count; i++)
                    {
                        if (frase.Palabras[i].InformacionMorfologica != null)
                        {
                            fraseCanonicas += frase.Palabras[i].InformacionMorfologica[0].InfoCanonica.FormaCanonica + " ";
                            fraseCategorias += categorias[frase.Palabras[i].InformacionMorfologica[0].InfoCanonica.IdCategoria].CategoriaAbrevEs + "#";
                        }
                        else
                        {
                            fraseCanonicas += frase.Palabras[i].Palabra + " ";
                            fraseCategorias += "desc." + "#";
                        }
                    }
                    frasesCanónicas.Enqueue(fraseCanonicas.Trim());
                    frasesCategorías.Enqueue(fraseCategorias.Trim());
                }
                if (wordsPhrasesCanonica.ContainsKey(word))
                {
                    foreach (string frase in frasesCanónicas)
                    {
                        wordsPhrasesCanonica[word].Enqueue(frase);
                    }
                } else
                {
                    wordsPhrasesCanonica.TryAdd(word, frasesCanónicas);
                }
                if (wordsPhrasesCategorias.ContainsKey(word))
                {
                    foreach (string frase in frasesCategorías)
                    {
                        wordsPhrasesCategorias[word].Enqueue(frase);
                    }
                }
                else
                {
                    wordsPhrasesCategorias.TryAdd(word, frasesCategorías);
                }
            });
        }
        private void Disambiguate(ConcurrentDictionary<nGram, int> nGramCounts, string phrase, string targetWord, int occurrence = 1)
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
                Disambiguate(nGramCounts, phrase, targetWord, occurrence+1);
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
                while (nGram <= 4)
                {
                    nGram result = ProcessNGram(GetNGramAtDistance(i + 1, nGram, dividedPhrase, "left"), targetWord, "left", nGram, i + 1, nGramCounts);
                    if (result.contents == "") break;
                    nGram++;
                }
                dividedPhrase = nGramsRight.ToArray();
                nGram = 1;
                while (nGram <= 4)
                {
                    nGram result = ProcessNGram(GetNGramAtDistance(i + 1, nGram, dividedPhrase, "right"), targetWord, "right", nGram, i + 1, nGramCounts);
                    if (result.contents == "") break;
                    nGram++;
                }
            }
            return;
        }
        private string GetNGramAtDistance(int distance, int nGram, string[] dividedPhrase, string orientation)
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
        private nGram ProcessNGram(string processedGram, string targetWord, string orientation, int nGram, int distance, ConcurrentDictionary<nGram, int> nGramCounts)
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
        private void CheckAmbiguousWordsInPhrase(string phrase)
        {
            string[] phraseWords = phrase.Split(" ");
            for (int i = 0; i < phraseWords.Length; i++)
            {
                string word = phraseWords[i];
                string regexedWord = wordRegex.Match(word).ToString().ToLower();
                if (wordsPhrases.ContainsKey(regexedWord))
                {
                    wordsPositionsInPhrases.Add(new WordPos(regexedWord, i));
                    if (!wordsPhrases[regexedWord].Contains(phrase))
                    {
                        wordsPhrases[regexedWord].Enqueue(phrase);
                    }
                }
            }
            return;
        }

        private void WriteWordsPhrasesToFile(string word, string fileName, ConcurrentDictionary<string, ConcurrentQueue<string>> targetDict)
        {
            StreamWriter streamWriter = null;
            try
            {
                streamWriter = new StreamWriter(baseDirectory + @"desambiguación\" + word + @"\" + fileName + ".txt", append: true);
                foreach (string phrase in targetDict[word].ToList())
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

        private void Button2_Click(object sender, EventArgs e)
        {
            DialogResult result = folderBrowserDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                baseDirectory = folderBrowserDialog1.SelectedPath + @"\";
                textBox1.Text = baseDirectory;
                button1.Enabled = true;
            }
        }

        private async void Button1_Click(object sender, EventArgs e)
        {
            await work();
        }
        private ParamtextFrase CreateParamtextFrase(string sentenceText, List<string> palabras)
        {
            ParamtextFrase frase = new ParamtextFrase();
            frase.Frase = sentenceText;
            frase.Palabras = palabras.Select(x => CreateParamtextPalabra(x)).ToList();
            return frase;
        }

        private ParamtextPalabra CreateParamtextPalabra(string palabra)
        {
            string matchedPalabra = wordRegex.Match(palabra.ToLower()).ToString().ToLower();
            ParamtextPalabra ParamtextPalabra = new ParamtextPalabra();
            ParamtextPalabra.Palabra = matchedPalabra;
            return ParamtextPalabra;
        }
    }
}
